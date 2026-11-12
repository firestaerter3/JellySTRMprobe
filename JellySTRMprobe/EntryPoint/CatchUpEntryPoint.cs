using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JellySTRMprobe.Service;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellySTRMprobe.EntryPoint;

/// <summary>
/// Background service that auto-probes new STRM items when they are added during library scans.
/// </summary>
public class CatchUpEntryPoint : IHostedService, IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(30);

    private readonly ILibraryManager _libraryManager;
    private readonly IProbeService _probeService;
    private readonly IPlaybackGate _playbackGate;
    private readonly ILogger<CatchUpEntryPoint> _logger;
    private readonly ConcurrentQueue<BaseItem> _pendingItems = new();

    private CancellationTokenSource? _stoppingCts;
    private Timer? _debounceTimer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatchUpEntryPoint"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="probeService">Instance of the <see cref="IProbeService"/>.</param>
    /// <param name="playbackGate">Instance of the <see cref="IPlaybackGate"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{CatchUpEntryPoint}"/>.</param>
    public CatchUpEntryPoint(
        ILibraryManager libraryManager,
        IProbeService probeService,
        IPlaybackGate playbackGate,
        ILogger<CatchUpEntryPoint> logger)
    {
        _libraryManager = libraryManager;
        _probeService = probeService;
        _playbackGate = playbackGate;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = new CancellationTokenSource();
        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        _libraryManager.ItemAdded += OnItemAdded;

        _logger.LogInformation("STRM Probe catch-up mode initialized");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);

#pragma warning disable CA1849 // CancelAsync causes BadImageFormatException in Jellyfin's dispose pipeline
        _stoppingCts?.Cancel();
#pragma warning restore CA1849

        _logger.LogInformation("STRM Probe catch-up mode stopped");
        return Task.CompletedTask;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (!Plugin.Instance.Configuration.EnableCatchUpMode)
        {
            return;
        }

        var item = e.Item;

        if (!LibraryScope.IsStrm(item))
        {
            return;
        }

        try
        {
            if (!LibraryScope.IsInSelectedLibraries(_libraryManager, item, Plugin.Instance.Configuration.SelectedLibraryIds))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Catch-up: could not determine the library of {ItemId}; skipping it", item.Id);
            return;
        }

        _pendingItems.Enqueue(item);
        _debounceTimer?.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
    }

    // Timer callbacks must be void. The try-catch ensures exceptions from the async
    // processing do not crash the process — they are logged instead.
    private async void OnDebounceElapsed(object? state)
    {
        try
        {
            await ProcessQueueAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Catch-up queue processing was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing catch-up queue");
        }
    }

    private async Task ProcessQueueAsync()
    {
        var items = new List<BaseItem>();

        while (_pendingItems.TryDequeue(out var item))
        {
            items.Add(item);
        }

        if (items.Count == 0)
        {
            return;
        }

        // Filter out items that already have media streams
        var unprobed = items
            .Where(item => item.GetMediaStreams().Count == 0)
            .ToList();

        if (unprobed.Count == 0)
        {
            _logger.LogDebug("Catch-up: all {Count} queued items already have media streams", items.Count);
            return;
        }

        var config = Plugin.Instance.Configuration;
        config.Validate();

        if (unprobed.Count > config.MaxItemsPerRun)
        {
            _logger.LogInformation(
                "Catch-up: {Count} new STRM items, probing the first {Max}; the scheduled task picks up the rest",
                unprobed.Count,
                config.MaxItemsPerRun);
            unprobed = unprobed.Take(config.MaxItemsPerRun).ToList();
        }

        _logger.LogInformation("Catch-up: probing {Count} new STRM items", unprobed.Count);

        var progress = new Progress<double>();
        var token = _stoppingCts?.Token ?? CancellationToken.None;

        var options = new ProbeBatchOptions
        {
            Parallelism = config.ProbeParallelism,
            TimeoutSeconds = config.ProbeTimeoutSeconds,
            CooldownMs = config.ProbeCooldownMs,
            MaxConsecutiveFailures = config.MaxConsecutiveFailures,
            BeforeEachAsync = config.PauseWhileStrmPlaying ? _playbackGate.WaitUntilNoStrmPlayingAsync : null,
        };

        var result = await _probeService.ProbeBatchAsync(unprobed, options, progress, token).ConfigureAwait(false);

        if (result.StoppedOnFailures && config.DeleteFailedStrms)
        {
            _logger.LogWarning("Catch-up: stopped on consecutive failures; no STRM files are deleted");
        }
        else if (config.DeleteFailedStrms && result.FailedItems.Count > 0)
        {
            var totalProbed = result.Probed + result.Failed;
            var failurePercent = (double)result.Failed / totalProbed * 100;

            if (failurePercent > config.DeleteFailureThreshold)
            {
                _logger.LogWarning(
                    "Catch-up: failure rate {Rate:F1}% exceeds threshold {Threshold}% — skipping deletion of {Count} STRM files (provider may be down)",
                    failurePercent,
                    config.DeleteFailureThreshold,
                    result.FailedItems.Count);
            }
            else
            {
                var deleted = _probeService.DeleteStrmFiles(result.FailedItems);
                _logger.LogInformation("Catch-up: deleted {Deleted} failed STRM files ({Rate:F1}% failure rate)", deleted, failurePercent);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and optionally managed resources.
    /// </summary>
    /// <param name="disposing">True to release both managed and unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _libraryManager.ItemAdded -= OnItemAdded;
            _stoppingCts?.Cancel();
            _stoppingCts?.Dispose();
            _debounceTimer?.Dispose();
        }

        _disposed = true;
    }
}
