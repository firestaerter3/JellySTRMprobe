using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using JellySTRMprobe.Service;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellySTRMprobe.Cache;

/// <summary>
/// Keeps a copy of the stream information of probed STRM items outside Jellyfin's database, and
/// puts it back when an item loses it, without asking the upstream server.
/// </summary>
/// <remarks>
/// Why: when the source behind the STRM files rebuilds its catalogue, a STRM generator can
/// remove and recreate every item, and every probe result is gone. Probing them all again takes
/// weeks at a pace an upstream server tolerates. The cache brings them back in minutes.
/// </remarks>
public class StreamCacheService : IStreamCache, IHostedService, IDisposable
{
    private static readonly TimeSpan SeedDelay = TimeSpan.FromMinutes(2);

    private readonly ILibraryManager _libraryManager;
    private readonly IMediaStreamRepository _mediaStreamRepository;
    private readonly ILogger<StreamCacheService> _logger;
    private readonly ConcurrentDictionary<Guid, byte> _restoring = new();
    private readonly StreamCacheStore _store;

    // Library events are handled on a background worker: the event comes in on the thread that
    // changed the item (often a parallel library scan), and file IO there would slow the scan.
    private readonly Channel<BaseItem> _queue = Channel.CreateUnbounded<BaseItem>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, byte> _queued = new();
    private CancellationTokenSource? _stoppingCts;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamCacheService"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="mediaStreamRepository">Instance of the <see cref="IMediaStreamRepository"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{StreamCacheService}"/> interface.</param>
    public StreamCacheService(
        ILibraryManager libraryManager,
        IMediaStreamRepository mediaStreamRepository,
        ILogger<StreamCacheService> logger)
        : this(libraryManager, mediaStreamRepository, logger, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamCacheService"/> class with a store, for tests.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="mediaStreamRepository">Instance of the <see cref="IMediaStreamRepository"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{StreamCacheService}"/> interface.</param>
    /// <param name="store">The store, or <c>null</c> for the plugin's data folder.</param>
    internal StreamCacheService(
        ILibraryManager libraryManager,
        IMediaStreamRepository mediaStreamRepository,
        ILogger<StreamCacheService> logger,
        StreamCacheStore? store)
    {
        _libraryManager = libraryManager;
        _mediaStreamRepository = mediaStreamRepository;
        _logger = logger;
        _store = store ?? new StreamCacheStore(() => Path.Combine(Plugin.Instance.DataFolderPath, "streamcache"), logger);
    }

    /// <inheritdoc />
    public bool NeedsVerification(BaseItem item)
    {
        if (!IsEnabled())
        {
            return false;
        }

        return _store.Get(item)?.NeedsVerification == true;
    }

    /// <inheritdoc />
    public void MarkVerified(BaseItem item)
    {
        try
        {
            var entry = _store.Get(item);
            if (entry is { NeedsVerification: true })
            {
                entry.NeedsVerification = false;
                _store.Put(item, entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Stream cache: cannot mark {ItemId} as verified", item.Id);
        }
    }

    /// <inheritdoc />
    public bool Save(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!IsEnabled() || !LibraryScope.IsStrm(item) || _restoring.ContainsKey(item.Id))
        {
            return false;
        }

        var streams = item.GetMediaStreams();
        if (!streams.Any(s => s.Type == MediaStreamType.Video))
        {
            return false;
        }

        var fingerprint = StreamCacheStore.Fingerprint(streams, item.RunTimeTicks, item.Container, item.TotalBitrate, item.Width, item.Height);
        var existing = _store.Get(item);
        if (existing != null && string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        item.ProviderIds.TryGetValue(MetadataProvider.Tmdb.ToString(), out var tmdb);
        _store.Put(item, new StreamCacheEntry
        {
            Path = item.Path,
            TmdbId = tmdb,
            SavedUtc = DateTime.UtcNow,
            Fingerprint = fingerprint,
            RunTimeTicks = item.RunTimeTicks,
            Container = item.Container,
            TotalBitrate = item.TotalBitrate,
            Width = item.Width,
            Height = item.Height,
            NeedsVerification = false,
            Streams = streams.ToList(),
        });
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TryRestoreAsync(BaseItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!IsEnabled() || !LibraryScope.IsStrm(item))
        {
            return false;
        }

        // Take the guard first, so the checks below and the write cannot interleave with a
        // second restore of the same item.
        if (!_restoring.TryAdd(item.Id, 0))
        {
            return false;
        }

        try
        {
            var entry = _store.Get(item);
            if (entry == null || !entry.Streams.Any(s => s.Type == MediaStreamType.Video))
            {
                return false;
            }

            // Mark the entry before touching Jellyfin: if anything below fails half-way, the
            // item gets an extra real probe instead of being trusted without one.
            if (Plugin.Instance.Configuration.VerifyAfterRestore && !entry.NeedsVerification)
            {
                entry.NeedsVerification = true;
                _store.Put(item, entry);
            }

            // Read the item's streams as late as possible: a probe that stored fresh streams in
            // the meantime wins, and nothing is overwritten.
            if (item.GetMediaStreams().Any(s => s.Type == MediaStreamType.Video))
            {
                return false;
            }

            _mediaStreamRepository.SaveMediaStreams(item.Id, entry.Streams, cancellationToken);
            item.RunTimeTicks = entry.RunTimeTicks;
            item.Container = entry.Container;
            item.TotalBitrate = entry.TotalBitrate;
            item.Width = entry.Width;
            item.Height = entry.Height;
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataImport, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Stream cache: restored {Count} streams for {ItemName} without a probe", entry.Streams.Count, item.Name);
            return true;
        }
        finally
        {
            _restoring.TryRemove(item.Id, out _);
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = new CancellationTokenSource();
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;
        _ = ProcessQueueAsync(_stoppingCts.Token);
        _ = SeedAsync(_stoppingCts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;
#pragma warning disable CA1849 // CancelAsync causes BadImageFormatException in Jellyfin's dispose pipeline
        _stoppingCts?.Cancel();
#pragma warning restore CA1849
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources.
    /// </summary>
    /// <param name="disposing">True to release managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (disposing)
        {
            _libraryManager.ItemAdded -= OnItemChanged;
            _libraryManager.ItemUpdated -= OnItemChanged;
            _stoppingCts?.Cancel();
            _stoppingCts?.Dispose();
        }
    }

    private static bool IsEnabled()
    {
        try
        {
            return Plugin.Instance.Configuration.EnableStreamCache;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool InScope(BaseItem item)
    {
        return LibraryScope.IsStrm(item)
            && LibraryScope.IsInSelectedLibraries(_libraryManager, item, Plugin.Instance.Configuration.SelectedLibraryIds);
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        try
        {
            var item = e.Item;
            if (item == null || !IsEnabled() || _restoring.ContainsKey(item.Id) || !LibraryScope.IsStrm(item))
            {
                return;
            }

            // One pending entry per item; the worker reads the item's state when it gets to it.
            if (_queued.TryAdd(item.Id, 0))
            {
                _queue.Writer.TryWrite(item);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stream cache: queueing a library change failed");
        }
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                _queued.TryRemove(item.Id, out _);
                try
                {
                    if (_restoring.ContainsKey(item.Id) || !InScope(item))
                    {
                        continue;
                    }

                    if (!Save(item))
                    {
                        await TryRestoreAsync(item, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Stream cache: handling a change of {ItemId} failed", item.Id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Server stopping.
        }
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SeedDelay, cancellationToken).ConfigureAwait(false);
            if (!IsEnabled())
            {
                return;
            }

            var query = new InternalItemsQuery();
            var selected = Plugin.Instance.Configuration.SelectedLibraryIds;
            if (selected.Length > 0)
            {
                query.AncestorIds = selected;
            }

            var saved = 0;
            var restored = 0;
            foreach (var id in _libraryManager.GetItemIds(query))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = _libraryManager.GetItemById(id);
                if (item == null || !LibraryScope.IsStrm(item))
                {
                    continue;
                }

                if (Save(item))
                {
                    saved++;
                }
                else if (await TryRestoreAsync(item, cancellationToken).ConfigureAwait(false))
                {
                    restored++;
                }
            }

            _logger.LogInformation("Stream cache: start-up pass wrote {Saved} entries and restored {Restored} items", saved, restored);
        }
        catch (OperationCanceledException)
        {
            // Server stopping.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stream cache: start-up pass failed");
        }
    }
}
