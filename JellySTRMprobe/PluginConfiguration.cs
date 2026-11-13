using System;
using MediaBrowser.Model.Plugins;

namespace JellySTRMprobe;

/// <summary>
/// Plugin configuration for JellySTRMprobe.
/// </summary>
/// <remarks>
/// The defaults are chosen for an IPTV provider that allows a single VOD connection at a time:
/// one probe at a time, a pause between probes, a cap per run, and nothing that starts on its own.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether playback of an already probed STRM item skips
    /// Jellyfin's probe and metadata refresh.
    /// </summary>
    /// <remarks>
    /// Jellyfin runs a full metadata refresh with a remote probe on every playback of a STRM file,
    /// even when the media streams are already known. That refresh took about 10 seconds per start
    /// on the test server. Items without a known video stream are never skipped: without stream
    /// information Jellyfin cannot direct play and falls back to a transcode that fails.
    /// </remarks>
    public bool EnableFastStart { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether catch-up mode is enabled.
    /// When enabled, new STRM items in the selected libraries are probed shortly after they are added.
    /// </summary>
    public bool EnableCatchUpMode { get; set; }

    /// <summary>
    /// Gets or sets the number of parallel probe operations.
    /// Each probe holds one connection to the upstream server.
    /// </summary>
    public int ProbeParallelism { get; set; } = 1;

    /// <summary>
    /// Gets or sets the per-item probe timeout in seconds.
    /// </summary>
    public int ProbeTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the cooldown delay in milliseconds after each probe operation.
    /// </summary>
    public int ProbeCooldownMs { get; set; } = 2000;

    /// <summary>
    /// Gets or sets the largest random extra pause in milliseconds after each probe, on top of
    /// <see cref="ProbeCooldownMs"/>. The pause is <c>cooldown + random(0..jitter)</c>, so the
    /// probes do not arrive at the upstream server in a fixed rhythm. 0 means a fixed pause.
    /// </summary>
    public int ProbeCooldownJitterMs { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the scheduled task picks a random selection of
    /// unprobed items each run instead of the library's own order.
    /// </summary>
    /// <remarks>
    /// On by default: the library order is roughly alphabetical, which makes a run a recognisable
    /// A-B-C walk through the catalogue. Shuffling costs nothing, since every item gets its turn
    /// over the runs anyway.
    /// </remarks>
    public bool ShuffleOrder { get; set; } = true;

    /// <summary>
    /// Gets or sets the largest random delay in minutes before a scheduled run starts probing,
    /// so runs do not start at the same minute every night. The delay counts towards the
    /// trigger's maximum run time. 0 means no delay.
    /// </summary>
    public int StartDelayMaxMinutes { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of items probed in one run of the task or one catch-up batch.
    /// The remaining items are left for the next run.
    /// </summary>
    public int MaxItemsPerRun { get; set; } = 500;

    /// <summary>
    /// Gets or sets the number of consecutive failed probes after which a run stops.
    /// A run of failures usually means the upstream server is down, not that every item is broken.
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 10;

    /// <summary>
    /// Gets or sets a value indicating whether probing waits while someone is playing a STRM item.
    /// </summary>
    /// <remarks>
    /// A probe opens a connection to the same upstream server as the viewer. With a provider that
    /// allows one connection, a probe next to a viewer makes one of the two fail.
    /// </remarks>
    public bool PauseWhileStrmPlaying { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether failed STRM files should be deleted after probing.
    /// When enabled, STRM files that fail to probe (dead/unavailable streams) are removed from disk.
    /// Files are recreated on the next library sync, giving them a fresh chance nightly.
    /// </summary>
    public bool DeleteFailedStrms { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum failure percentage before deletion is skipped.
    /// If the failure rate exceeds this threshold, no files are deleted (provider may be down).
    /// </summary>
    public int DeleteFailureThreshold { get; set; } = 10;

    /// <summary>
    /// Gets or sets the array of selected library IDs.
    /// Applies to the scheduled task, catch-up mode and fast start. Empty array means all libraries.
    /// </summary>
    public Guid[] SelectedLibraryIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// Validates and clamps all configuration values to safe ranges.
    /// </summary>
    public void Validate()
    {
        ProbeParallelism = Math.Clamp(ProbeParallelism, 1, 20);
        ProbeTimeoutSeconds = Math.Clamp(ProbeTimeoutSeconds, 10, 300);
        ProbeCooldownMs = Math.Clamp(ProbeCooldownMs, 0, 60000);
        ProbeCooldownJitterMs = Math.Clamp(ProbeCooldownJitterMs, 0, 300000);
        StartDelayMaxMinutes = Math.Clamp(StartDelayMaxMinutes, 0, 240);
        MaxItemsPerRun = Math.Clamp(MaxItemsPerRun, 1, 1000000);
        MaxConsecutiveFailures = Math.Clamp(MaxConsecutiveFailures, 1, 10000);
        DeleteFailureThreshold = Math.Clamp(DeleteFailureThreshold, 1, 100);
        SelectedLibraryIds ??= Array.Empty<Guid>();
    }
}
