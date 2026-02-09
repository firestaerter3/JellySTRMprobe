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
    /// <returns>A tuple of (Probed, Failed) counts.</returns>
    Task<(int Probed, int Failed)> ProbeBatchAsync(
        IReadOnlyList<BaseItem> items,
        int parallelism,
        int timeoutSeconds,
        int cooldownMs,
        IProgress<double> progress,
        CancellationToken cancellationToken);
}
