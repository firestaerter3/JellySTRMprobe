using System;
using MediaBrowser.Model.Plugins;

namespace JellySTRMprobe;

/// <summary>
/// Plugin configuration for JellySTRMprobe.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether catch-up mode is enabled.
    /// When enabled, new STRM items are automatically probed when added during library scans.
    /// </summary>
    public bool EnableCatchUpMode { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of parallel probe operations.
    /// Higher values speed up probing but increase load on upstream servers.
    /// </summary>
    public int ProbeParallelism { get; set; } = 5;

    /// <summary>
    /// Gets or sets the per-item probe timeout in seconds.
    /// </summary>
    public int ProbeTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the cooldown delay in milliseconds between probe operations.
    /// Helps prevent connection exhaustion on upstream servers.
    /// </summary>
    public int ProbeCooldownMs { get; set; } = 200;

    /// <summary>
    /// Gets or sets a value indicating whether failed STRM files should be deleted after probing.
    /// When enabled, STRM files that fail to probe (dead/unavailable streams) are removed from disk.
    /// Files are recreated on the next library sync, giving them a fresh chance nightly.
    /// </summary>
    public bool DeleteFailedStrms { get; set; } = false;

    /// <summary>
    /// Gets or sets the array of selected library IDs to probe.
    /// Empty array means probe all libraries.
    /// </summary>
    public Guid[] SelectedLibraryIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// Validates and clamps all configuration values to safe ranges.
    /// </summary>
    public void Validate()
    {
        ProbeParallelism = Math.Clamp(ProbeParallelism, 1, 20);
        ProbeTimeoutSeconds = Math.Clamp(ProbeTimeoutSeconds, 10, 300);
        ProbeCooldownMs = Math.Clamp(ProbeCooldownMs, 0, 5000);
    }
}
