using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace JellySTRMprobe.Service;

/// <summary>
/// Result of a batch probe operation.
/// </summary>
public class ProbeResult
{
    /// <summary>
    /// Gets the number of items that were successfully probed.
    /// </summary>
    public int Probed { get; init; }

    /// <summary>
    /// Gets the number of items that failed to probe.
    /// </summary>
    public int Failed { get; init; }

    /// <summary>
    /// Gets the list of items that failed to probe.
    /// </summary>
    public IReadOnlyList<BaseItem> FailedItems { get; init; } = [];
}
