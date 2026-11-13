using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JellySTRMprobe.Cache;
using JellySTRMprobe.Ordering;
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
    private readonly IStreamCache _streamCache;
    private readonly ILogger<ProbeStrmTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProbeStrmTask"/> class.
    /// </summary>
    /// <param name="probeService">Instance of the <see cref="IProbeService"/>.</param>
    /// <param name="playbackGate">Instance of the <see cref="IPlaybackGate"/>.</param>
    /// <param name="streamCache">Instance of the <see cref="IStreamCache"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{ProbeStrmTask}"/>.</param>
    public ProbeStrmTask(IProbeService probeService, IPlaybackGate playbackGate, IStreamCache streamCache, ILogger<ProbeStrmTask> logger)
    {
        _probeService = probeService;
        _playbackGate = playbackGate;
        _streamCache = streamCache;
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

    /// <summary>
    /// Gets or sets the delay used for the random start delay. Replaceable in tests.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    /// <summary>
    /// Gets or sets the random source, returning an integer in [min, max). Replaceable in tests.
    /// </summary>
    internal Func<int, int, int> NextInt { get; set; } = Random.Shared.Next;

    /// <inheritdoc />
    /// <remarks>
    /// No trigger by default: every probe opens a connection to the upstream server, so when and
    /// how long the task runs is left to the administrator (Dashboard, Scheduled Tasks).
    /// </remarks>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Array.Empty<TaskTriggerInfo>();
    }

    private static void Shuffle(BaseItem[] items) => Random.Shared.Shuffle(items);

    /// <summary>
    /// Picks the items for one run: optionally reordered, then cut to the maximum.
    /// </summary>
    /// <param name="all">All unprobed items.</param>
    /// <param name="max">The maximum per run.</param>
    /// <param name="reorder">Reorders the list in place, or <c>null</c> to keep the library order.</param>
    /// <returns>The items to probe in this run.</returns>
    internal static IReadOnlyList<BaseItem> SelectItems(IReadOnlyList<BaseItem> all, int max, Action<BaseItem[]>? reorder)
    {
        if (reorder == null)
        {
            return all.Count > max ? all.Take(max).ToList() : all;
        }

        var copy = all.ToArray();
        reorder(copy);
        return copy.Length > max ? copy.Take(max).ToList() : copy;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance.Configuration;
        config.Validate();

        _logger.LogInformation("Starting STRM probe task");

        var unprobed = _probeService.GetUnprobedItems(config.SelectedLibraryIds);

        // Items whose streams are in the cache come back without a request to the upstream server.
        var allItems = new List<BaseItem>(unprobed.Count);
        var restored = 0;
        foreach (var item in unprobed)
        {
            if (config.EnableStreamCache && await _streamCache.TryRestoreAsync(item, cancellationToken).ConfigureAwait(false))
            {
                restored++;
            }
            else
            {
                allItems.Add(item);
            }
        }

        if (restored > 0)
        {
            _logger.LogInformation("Restored {Count} items from the stream cache without probing", restored);
        }

        var items = config.PrioritizeOrder
            ? SelectItems(PriorityOrder.Sort(allItems, config, DateTime.UtcNow, Random.Shared), config.MaxItemsPerRun, null)
            : SelectItems(allItems, config.MaxItemsPerRun, config.ShuffleOrder ? Shuffle : null);

        if (items.Count == 0)
        {
            _logger.LogInformation("No unprobed STRM items found");
            progress.Report(100);
            return;
        }

        _logger.LogInformation(
            "Probing {Count} of {Total} unprobed items ({Order} order) with parallelism={Parallelism}, timeout={Timeout}s, cooldown={Cooldown}ms + random 0-{Jitter}ms, stop after {MaxFailures} failures in a row",
            items.Count,
            allItems.Count,
            config.PrioritizeOrder ? "priority" : config.ShuffleOrder ? "random" : "library",
            config.ProbeParallelism,
            config.ProbeTimeoutSeconds,
            config.ProbeCooldownMs,
            config.ProbeCooldownJitterMs,
            config.MaxConsecutiveFailures);

        if (config.StartDelayMaxMinutes > 0)
        {
            var startDelay = TimeSpan.FromSeconds(NextInt(0, (config.StartDelayMaxMinutes * 60) + 1));
            _logger.LogInformation("Waiting {Minutes:F1} minutes before the first probe (random start delay)", startDelay.TotalMinutes);
            await Delay(startDelay, cancellationToken).ConfigureAwait(false);
        }

        var options = new ProbeBatchOptions
        {
            Parallelism = config.ProbeParallelism,
            TimeoutSeconds = config.ProbeTimeoutSeconds,
            CooldownMs = config.ProbeCooldownMs,
            CooldownJitterMs = config.ProbeCooldownJitterMs,
            ProbeScope = config.EnableStreamCache ? item => _streamCache.BeginProbe(item.Id) : null,
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
