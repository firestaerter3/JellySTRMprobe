using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace JellySTRMprobe.Service;

/// <summary>
/// Core service for probing STRM files to extract media information.
/// </summary>
public class ProbeService : IProbeService
{
    private readonly IProviderManager _providerManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ProbeService> _logger;

    private IMetadataProvider? _probeProvider;
    private bool _probeProviderResolved;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProbeService"/> class.
    /// </summary>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{ProbeService}"/> interface.</param>
    public ProbeService(
        IProviderManager providerManager,
        ILibraryManager libraryManager,
        IFileSystem fileSystem,
        ILogger<ProbeService> logger)
    {
        _providerManager = providerManager;
        _libraryManager = libraryManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<BaseItem> GetUnprobedItems(Guid[] selectedLibraryIds)
    {
        // Jellyfin 10.11.x EF Core: TopParentIds and MediaTypes filters return 0
        // results. AncestorIds uses a join table and works correctly.
        var query = new InternalItemsQuery();

        if (selectedLibraryIds.Length > 0)
        {
            query.AncestorIds = selectedLibraryIds;
        }

        var itemIds = _libraryManager.GetItemIds(query);
        _logger.LogInformation("Query returned {Count} item IDs", itemIds.Count);

        var unprobedItems = new List<BaseItem>();
        var totalResolved = 0;

        foreach (var id in itemIds)
        {
            BaseItem? item;
            try
            {
                item = _libraryManager.GetItemById(id);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipping item {ItemId}: failed to resolve", id);
                continue;
            }

            if (item == null)
            {
                continue;
            }

            totalResolved++;

            if (item.Path != null
                && item.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)
                && item.GetMediaStreams().Count == 0)
            {
                unprobedItems.Add(item);
            }
        }

        _logger.LogInformation(
            "Found {Count} unprobed STRM items out of {Total} resolved ({TotalIds} IDs queried)",
            unprobedItems.Count,
            totalResolved,
            itemIds.Count);

        return unprobedItems;
    }

    /// <inheritdoc />
    public async Task<bool> ProbeItemAsync(BaseItem item, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var directoryService = new DirectoryService(_fileSystem);
        return await ProbeItemCoreAsync(item, timeoutSeconds, directoryService, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ProbeDirectAsync(BaseItem item, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
            {
                EnableRemoteContentProbe = true,
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            };
            return await TryDirectProbeAsync(item, options, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Verification probe timed out for {ItemName} ({ItemId})", item.Name, item.Id);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Verification probe failed for {ItemName} ({ItemId})", item.Name, item.Id);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<ProbeResult> ProbeBatchAsync(
        IReadOnlyList<BaseItem> items,
        int parallelism,
        int timeoutSeconds,
        int cooldownMs,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var options = new ProbeBatchOptions
        {
            Parallelism = parallelism,
            TimeoutSeconds = timeoutSeconds,
            CooldownMs = cooldownMs,
        };

        return ProbeBatchAsync(items, options, progress, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ProbeResult> ProbeBatchAsync(
        IReadOnlyList<BaseItem> items,
        ProbeBatchOptions options,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (items.Count == 0)
        {
            progress.Report(100);
            return new ProbeResult();
        }

        var probed = 0;
        var failed = 0;
        var processed = 0;
        var consecutiveFailures = 0;
        var stoppedOnFailures = false;
        var counterLock = new object();
        var failedItems = new ConcurrentBag<BaseItem>();
        var directoryService = new DirectoryService(_fileSystem);

        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await Parallel.ForEachAsync(
                items,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, options.Parallelism),
                    CancellationToken = stopCts.Token,
                },
                async (item, ct) =>
                {
                    if (options.BeforeEachAsync != null)
                    {
                        await options.BeforeEachAsync(ct).ConfigureAwait(false);
                    }

                    bool success;
                    using (options.ProbeScope?.Invoke(item))
                    {
                        success = await ProbeItemCoreAsync(item, options.TimeoutSeconds, directoryService, ct).ConfigureAwait(false);
                    }

                    // Count and check under one lock, so "in a row" follows the order in which
                    // probes finish, also when several run at the same time.
                    var stopNow = false;
                    lock (counterLock)
                    {
                        if (success)
                        {
                            probed++;
                            consecutiveFailures = 0;
                        }
                        else
                        {
                            failed++;
                            failedItems.Add(item);
                            consecutiveFailures++;
                            if (options.MaxConsecutiveFailures > 0
                                && consecutiveFailures >= options.MaxConsecutiveFailures
                                && !stoppedOnFailures)
                            {
                                stoppedOnFailures = true;
                                stopNow = true;
                            }
                        }
                    }

                    if (stopNow)
                    {
                        _logger.LogWarning(
                            "Stopping probe run after {Count} failed probes in a row; the upstream server may be down",
                            options.MaxConsecutiveFailures);
#pragma warning disable CA1849 // CancelAsync causes BadImageFormatException in Jellyfin's dispose pipeline
                        stopCts.Cancel();
#pragma warning restore CA1849
                    }

                    var current = Interlocked.Increment(ref processed);
                    progress.Report((double)current / items.Count * 100);

                    var pause = PauseAfterProbe(options.CooldownMs, options.CooldownJitterMs, Random.Shared.Next);
                    if (pause > 0)
                    {
                        await Task.Delay(pause, ct).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && stoppedOnFailures)
        {
            // Stopped by the consecutive-failure limit, not by the caller.
        }

        int probedTotal;
        int failedTotal;
        bool stopped;
        lock (counterLock)
        {
            probedTotal = probed;
            failedTotal = failed;
            stopped = stoppedOnFailures;
        }

        _logger.LogInformation(
            "Probe complete: {Probed} succeeded, {Failed} failed out of {Total}{Stopped}",
            probedTotal,
            failedTotal,
            items.Count,
            stopped ? " (stopped early on consecutive failures)" : string.Empty);

        return new ProbeResult
        {
            Probed = probedTotal,
            Failed = failedTotal,
            FailedItems = failedItems.ToArray(),
            StoppedOnFailures = stopped,
        };
    }

    /// <summary>
    /// Returns the pause after a probe: the cooldown plus a random part between 0 and the jitter.
    /// </summary>
    /// <param name="cooldownMs">The fixed cooldown in milliseconds.</param>
    /// <param name="jitterMs">The largest random extra in milliseconds.</param>
    /// <param name="next">Returns a random integer in [min, max).</param>
    /// <returns>The pause in milliseconds, never negative.</returns>
    internal static int PauseAfterProbe(int cooldownMs, int jitterMs, Func<int, int, int> next)
    {
        var fixedPart = Math.Max(0, cooldownMs);
        if (jitterMs <= 0)
        {
            return fixedPart;
        }

        return fixedPart + next(0, jitterMs + 1);
    }

    /// <inheritdoc />
    public int DeleteStrmFiles(IReadOnlyList<BaseItem> items)
    {
        var deleted = 0;

        foreach (var item in items)
        {
            var path = item.Path;

            if (string.IsNullOrEmpty(path) || !path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Skipping non-STRM path for deletion: {Path}", path);
                continue;
            }

            try
            {
                File.Delete(path);
                deleted++;
                _logger.LogDebug("Deleted failed STRM file: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete STRM file: {Path}", path);
            }
        }

        return deleted;
    }

    private async Task<bool> ProbeItemCoreAsync(
        BaseItem item,
        int timeoutSeconds,
        DirectoryService directoryService,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var refreshOptions = new MetadataRefreshOptions(directoryService)
            {
                EnableRemoteContentProbe = true,
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            };

            // Try direct probe first — calls ProbeProvider.FetchAsync directly,
            // bypassing remote metadata providers (TMDb, etc.) for ~3x faster probing.
            if (await TryDirectProbeAsync(item, refreshOptions, timeoutCts.Token).ConfigureAwait(false))
            {
                _logger.LogDebug("Successfully probed {ItemName} ({ItemId}) via direct probe", item.Name, item.Id);
                return true;
            }

            // Fallback: use the full refresh pipeline (includes TMDb re-fetch).
            refreshOptions.ImageRefreshMode = MetadataRefreshMode.ValidationOnly;
            refreshOptions.ReplaceAllMetadata = false;
            refreshOptions.ReplaceAllImages = false;

            await _providerManager.RefreshSingleItem(item, refreshOptions, timeoutCts.Token).ConfigureAwait(false);

            _logger.LogDebug("Successfully probed {ItemName} ({ItemId}) via full refresh fallback", item.Name, item.Id);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Probe timed out for {ItemName} ({ItemId}) after {Timeout}s", item.Name, item.Id, timeoutSeconds);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Probe failed for {ItemName} ({ItemId})", item.Name, item.Id);
            return false;
        }
    }

    private async Task<bool> TryDirectProbeAsync(
        BaseItem item,
        MetadataRefreshOptions options,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProbeProvider(item);
        if (provider == null)
        {
            return false;
        }

        ItemUpdateType updateType;

        // Check specific types first (Movie/Episode extend Video).
        if (item is Movie movie && provider is ICustomMetadataProvider<Movie> movieProvider)
        {
            updateType = await movieProvider.FetchAsync(movie, options, cancellationToken).ConfigureAwait(false);
        }
        else if (item is Episode episode && provider is ICustomMetadataProvider<Episode> episodeProvider)
        {
            updateType = await episodeProvider.FetchAsync(episode, options, cancellationToken).ConfigureAwait(false);
        }
        else if (item is Video video && provider is ICustomMetadataProvider<Video> videoProvider)
        {
            updateType = await videoProvider.FetchAsync(video, options, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogDebug("Direct probe not supported for item type {Type}, falling back", item.GetType().Name);
            return false;
        }

        if (updateType > ItemUpdateType.None)
        {
            await item.UpdateToRepositoryAsync(updateType, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private IMetadataProvider? ResolveProbeProvider(BaseItem item)
    {
        if (!_probeProviderResolved)
        {
            // Resolve ProbeProvider via IProviderManager.GetMetadataProviders<T>().
            // This returns all configured providers for the item's type, including ProbeProvider.
            var libraryOptions = _libraryManager.GetLibraryOptions(item);

            IEnumerable<IMetadataProvider> providers = item switch
            {
                Movie => _providerManager.GetMetadataProviders<Movie>(item, libraryOptions),
                Episode => _providerManager.GetMetadataProviders<Episode>(item, libraryOptions),
                _ => _providerManager.GetMetadataProviders<Video>(item, libraryOptions),
            };

            _probeProvider = providers
                .FirstOrDefault(p => p.GetType().Name.Contains("Probe", StringComparison.OrdinalIgnoreCase));
            _probeProviderResolved = true;

            if (_probeProvider != null)
            {
                _logger.LogInformation("Resolved direct probe provider: {Name}", _probeProvider.GetType().FullName);
            }
            else
            {
                _logger.LogWarning("Could not resolve probe provider — falling back to full refresh pipeline");
            }
        }

        return _probeProvider;
    }
}
