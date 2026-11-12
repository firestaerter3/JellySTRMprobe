using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using JellySTRMprobe.Service;
using JellySTRMprobe.Tasks;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JellySTRMprobe.Tests.Tasks;

[Collection("PluginInstance")]
public class ProbeStrmTaskTests
{
    private readonly Mock<IProbeService> _mockProbeService;
    private readonly Mock<IPlaybackGate> _mockPlaybackGate;
    private readonly Mock<ILogger<ProbeStrmTask>> _mockLogger;
    private readonly ProbeStrmTask _task;

    public ProbeStrmTaskTests()
    {
        _mockProbeService = new Mock<IProbeService>();
        _mockPlaybackGate = new Mock<IPlaybackGate>();
        _mockLogger = new Mock<ILogger<ProbeStrmTask>>();

        TestHelpers.ResetConfiguration();

        _task = new ProbeStrmTask(_mockProbeService.Object, _mockPlaybackGate.Object, _mockLogger.Object);
    }

    private void SetupBatch(Action<IReadOnlyList<BaseItem>, ProbeBatchOptions>? capture = null, ProbeResult? result = null)
    {
        _mockProbeService
            .Setup(s => s.ProbeBatchAsync(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<ProbeBatchOptions>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<BaseItem> items, ProbeBatchOptions options, IProgress<double> _, CancellationToken _) => capture?.Invoke(items, options))
            .ReturnsAsync(result ?? new ProbeResult());
    }

    [Fact]
    public void Task_HasCorrectMetadata()
    {
        _task.Name.Should().Be("Probe STRM Media Info");
        _task.Key.Should().Be("StrmProbeMediaInfo");
        _task.Category.Should().Be("STRM Probe");
        _task.IsHidden.Should().BeFalse();
        _task.IsEnabled.Should().BeTrue();
        _task.IsLogged.Should().BeTrue();
    }

    [Fact]
    public void GetDefaultTriggers_ReturnsNone()
    {
        _task.GetDefaultTriggers().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithNoItems_ReportsFullProgress()
    {
        _mockProbeService
            .Setup(s => s.GetUnprobedItems(It.IsAny<Guid[]>()))
            .Returns(new List<BaseItem>());

        double? lastProgress = null;
        var progress = new SynchronousProgress(v => lastProgress = v);

        await _task.ExecuteAsync(progress, CancellationToken.None);

        lastProgress.Should().Be(100);
    }

    [Fact]
    public async Task ExecuteAsync_PassesConfigValues()
    {
        TestHelpers.ResetConfiguration(c =>
        {
            c.ProbeParallelism = 3;
            c.ProbeTimeoutSeconds = 120;
            c.ProbeCooldownMs = 500;
            c.MaxConsecutiveFailures = 7;
            c.PauseWhileStrmPlaying = true;
        });

        _mockProbeService
            .Setup(s => s.GetUnprobedItems(It.IsAny<Guid[]>()))
            .Returns(new List<BaseItem> { TestHelpers.CreateTestItem("Movie 1", "/m/1.strm") });

        ProbeBatchOptions? seen = null;
        SetupBatch((_, options) => seen = options, new ProbeResult { Probed = 1 });

        await _task.ExecuteAsync(new SynchronousProgress(_ => { }), CancellationToken.None);

        seen.Should().NotBeNull();
        seen!.Parallelism.Should().Be(3);
        seen.TimeoutSeconds.Should().Be(120);
        seen.CooldownMs.Should().Be(500);
        seen.MaxConsecutiveFailures.Should().Be(7);
        seen.BeforeEachAsync.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutPause_PassesNoWaitCallback()
    {
        TestHelpers.ResetConfiguration(c => c.PauseWhileStrmPlaying = false);

        _mockProbeService
            .Setup(s => s.GetUnprobedItems(It.IsAny<Guid[]>()))
            .Returns(new List<BaseItem> { TestHelpers.CreateTestItem("Movie 1", "/m/1.strm") });

        ProbeBatchOptions? seen = null;
        SetupBatch((_, options) => seen = options);

        await _task.ExecuteAsync(new SynchronousProgress(_ => { }), CancellationToken.None);

        seen!.BeforeEachAsync.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ProbesAtMostMaxItemsPerRun()
    {
        TestHelpers.ResetConfiguration(c => c.MaxItemsPerRun = 2);

        var items = Enumerable.Range(1, 5)
            .Select(i => TestHelpers.CreateTestItem("Movie " + i, "/m/" + i + ".strm"))
            .ToList<BaseItem>();
        _mockProbeService
            .Setup(s => s.GetUnprobedItems(It.IsAny<Guid[]>()))
            .Returns(items);

        IReadOnlyList<BaseItem>? probed = null;
        SetupBatch((list, _) => probed = list);

        await _task.ExecuteAsync(new SynchronousProgress(_ => { }), CancellationToken.None);

        probed.Should().NotBeNull();
        probed!.Should().HaveCount(2);
        probed.Should().Equal(items.Take(2));
    }

    [Fact]
    public async Task ExecuteAsync_StoppedOnFailures_DeletesNothing()
    {
        TestHelpers.ResetConfiguration(c =>
        {
            c.DeleteFailedStrms = true;
            c.DeleteFailureThreshold = 100;
        });

        var item = TestHelpers.CreateTestItem("Movie 1", "/m/1.strm");
        _mockProbeService
            .Setup(s => s.GetUnprobedItems(It.IsAny<Guid[]>()))
            .Returns(new List<BaseItem> { item });
        SetupBatch(result: new ProbeResult { Failed = 1, FailedItems = new[] { item }, StoppedOnFailures = true });

        await _task.ExecuteAsync(new SynchronousProgress(_ => { }), CancellationToken.None);

        _mockProbeService.Verify(s => s.DeleteStrmFiles(It.IsAny<IReadOnlyList<BaseItem>>()), Times.Never);
    }
}
