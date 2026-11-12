using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JellySTRMprobe.Service;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace JellySTRMprobe.Tasks;

/// <summary>
/// Scheduled task that probes unprobed STRM items to extract media information.
/// </summary>
public class ProbeStrmTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly IProbeService _probeService;
    private readonly IPlaybackGate _playbackGate;
    private readonly ILogger<ProbeStrmTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProbeStrmTask"/> class.
    /// </summary>
    /// <param name="probeService">Instance of the <see cref="IProbeService"/>.</param>
    /// <param name="playbackGate">Instance of the <see cref="IPlaybackGate"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{ProbeStrmTask}"/>.</param>
    public ProbeStrmTask(IProbeService probeService, IPlaybackGate playbackGate, ILogger<ProbeStrmTask> logger)
    {
        _probeService = probeService;
        _playbackGate = playbackGate;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Probe STRM Media Info";

    /// <inheritdoc />
    public string Key => "StrmProbeMediaInfo";

    /// <inheritdoc />
    public string Description => "Probes STRM file targets to extract media information (codec, resolution, duration, audio).";

    /// <inheritdoc />
    public string Category => "STRM Probe";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    /// <remarks>
    /// No trigger by default: every probe opens a connection to the upstream server, so when and
    /// how long the task runs is left to the administrator (Dashboard, Scheduled Tasks).
    /// </remarks>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Array.Empty<TaskTriggerInfo>();
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance.Configuration;
        config.Validate();

        _logger.LogInformation("Starting STRM probe task");

        var allItems = _probeService.GetUnprobedItems(config.SelectedLibraryIds);
        IReadOnlyList<BaseItem> items = allItems.Count > config.MaxItemsPerRun
            ? allItems.Take(config.MaxItemsPerRun).ToList()
            : allItems;

        if (items.Count == 0)
        {
            _logger.LogInformation("No unprobed STRM items found");
            progress.Report(100);
            return;
        }

        _logger.LogInformation(
            "Probing {Count} of {Total} unprobed items with parallelism={Parallelism}, timeout={Timeout}s, cooldown={Cooldown}ms, stop after {MaxFailures} failures in a row",
            items.Count,
            allItems.Count,
            config.ProbeParallelism,
            config.ProbeTimeoutSeconds,
            config.ProbeCooldownMs,
            config.MaxConsecutiveFailures);

        var options = new ProbeBatchOptions
        {
            Parallelism = config.ProbeParallelism,
            TimeoutSeconds = config.ProbeTimeoutSeconds,
            CooldownMs = config.ProbeCooldownMs,
            MaxConsecutiveFailures = config.MaxConsecutiveFailures,
            BeforeEachAsync = config.PauseWhileStrmPlaying ? _playbackGate.WaitUntilNoStrmPlayingAsync : null,
        };

        var result = await _probeService.ProbeBatchAsync(items, options, progress, cancellationToken).ConfigureAwait(false);

        if (result.StoppedOnFailures && config.DeleteFailedStrms)
        {
            _logger.LogWarning("Run stopped on consecutive failures; no STRM files are deleted");
        }
        else if (config.DeleteFailedStrms && result.FailedItems.Count > 0)
        {
            var totalProbed = result.Probed + result.Failed;
            var failurePercent = (double)result.Failed / totalProbed * 100;

            if (failurePercent > config.DeleteFailureThreshold)
            {
                _logger.LogWarning(
                    "Failure rate {Rate:F1}% exceeds threshold {Threshold}% — skipping deletion of {Count} STRM files (provider may be down)",
                    failurePercent,
                    config.DeleteFailureThreshold,
                    result.FailedItems.Count);
            }
            else
            {
                var deleted = _probeService.DeleteStrmFiles(result.FailedItems);
                _logger.LogInformation("Deleted {Deleted} failed STRM files ({Rate:F1}% failure rate)", deleted, failurePercent);
            }
        }

        _logger.LogInformation(
            "STRM probe task finished: {Probed} probed, {Failed} failed",
            result.Probed,
            result.Failed);
    }
}
