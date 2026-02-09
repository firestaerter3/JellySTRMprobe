using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
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
    public async Task<(int Probed, int Failed)> ProbeBatchAsync(
        IReadOnlyList<BaseItem> items,
        int parallelism,
        int timeoutSeconds,
        int cooldownMs,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            progress.Report(100);
            return (0, 0);
        }

        var probed = 0;
        var failed = 0;
        var processed = 0;
        var directoryService = new DirectoryService(_fileSystem);

        await Parallel.ForEachAsync(
            items,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelism,
                CancellationToken = cancellationToken,
            },
            async (item, ct) =>
            {
                var success = await ProbeItemCoreAsync(item, timeoutSeconds, directoryService, ct).ConfigureAwait(false);

                if (success)
                {
                    Interlocked.Increment(ref probed);
                }
                else
                {
                    Interlocked.Increment(ref failed);
                }

                var current = Interlocked.Increment(ref processed);
                progress.Report((double)current / items.Count * 100);

                if (cooldownMs > 0)
                {
                    await Task.Delay(cooldownMs, ct).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

        _logger.LogInformation(
            "Probe complete: {Probed} succeeded, {Failed} failed out of {Total}",
            probed,
            failed,
            items.Count);

        return (probed, failed);
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
                ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
                ReplaceAllMetadata = false,
                ReplaceAllImages = false,
            };

            await _providerManager.RefreshSingleItem(item, refreshOptions, timeoutCts.Token).ConfigureAwait(false);

            _logger.LogDebug("Successfully probed {ItemName} ({ItemId})", item.Name, item.Id);
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
}
