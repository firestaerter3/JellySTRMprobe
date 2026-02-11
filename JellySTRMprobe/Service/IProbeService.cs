using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace JellySTRMprobe.Service;

/// <summary>
/// Service for probing STRM files to extract media information.
/// </summary>
public interface IProbeService
{
    /// <summary>
    /// Gets all unprobed STRM items from the specified libraries.
    /// </summary>
    /// <param name="selectedLibraryIds">Library IDs to filter by. Empty array means all libraries.</param>
    /// <returns>A list of unprobed STRM items.</returns>
    IReadOnlyList<BaseItem> GetUnprobedItems(Guid[] selectedLibraryIds);

    /// <summary>
    /// Probes a single item to extract media information.
    /// </summary>
    /// <param name="item">The item to probe.</param>
    /// <param name="timeoutSeconds">Timeout in seconds for the probe operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the probe succeeded, false otherwise.</returns>
    Task<bool> ProbeItemAsync(BaseItem item, int timeoutSeconds, CancellationToken cancellationToken);

    /// <summary>
    /// Probes a batch of items in parallel with configurable concurrency, timeout, and cooldown.
    /// </summary>
    /// <param name="items">The items to probe.</param>
    /// <param name="parallelism">Maximum number of concurrent probe operations.</param>
    /// <param name="timeoutSeconds">Per-item timeout in seconds.</param>
    /// <param name="cooldownMs">Cooldown in milliseconds between probes per worker.</param>
    /// <param name="progress">Progress reporter (0-100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ProbeResult"/> with counts and failed items.</returns>
    Task<ProbeResult> ProbeBatchAsync(
        IReadOnlyList<BaseItem> items,
        int parallelism,
        int timeoutSeconds,
        int cooldownMs,
        IProgress<double> progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the STRM files on disk for the given items.
    /// Only deletes files with a .strm extension (safety check).
    /// Does not remove items from the Jellyfin database — the next library scan handles that.
    /// </summary>
    /// <param name="items">The items whose STRM files should be deleted.</param>
    /// <returns>The number of files successfully deleted.</returns>
    int DeleteStrmFiles(IReadOnlyList<BaseItem> items);
}
