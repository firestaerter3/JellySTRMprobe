using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using JellySTRMprobe.Service;
using JellySTRMprobe.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JellySTRMprobe.Tests.Tasks;

public class ProbeStrmTaskTests
{
    private readonly Mock<IProbeService> _mockProbeService;
    private readonly Mock<ILogger<ProbeStrmTask>> _mockLogger;
    private readonly ProbeStrmTask _task;

    public ProbeStrmTaskTests()
    {
        _mockProbeService = new Mock<IProbeService>();
        _mockLogger = new Mock<ILogger<ProbeStrmTask>>();

        TestHelpers.EnsurePluginInstance();

        _task = new ProbeStrmTask(_mockProbeService.Object, _mockLogger.Object);
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
    public void GetDefaultTriggers_ReturnsDailyAt4AM()
    {
        var triggers = _task.GetDefaultTriggers().ToList();

        triggers.Should().HaveCount(1);
        triggers[0].Type.Should().Be(TaskTriggerInfoType.DailyTrigger);
        triggers[0].TimeOfDayTicks.Should().Be(TimeSpan.FromHours(4).Ticks);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoItems_ReportsFullProgress()
    {
        _mockProbeService
            .Setup(s => s.GetUnprobedItems(It.IsAny<Guid[]>()))
            .Returns(new List<BaseItem>());

        double? lastProgress = null;
        var progress = new Progress<double>(v => lastProgress = v);

        await _task.ExecuteAsync(progress, CancellationToken.None);

        // Wait for async progress callback
        await Task.Delay(100);

        lastProgress.Should().Be(100);
    }

    [Fact]
    public async Task ExecuteAsync_WithItems_CallsProbeBatch()
    {
        var items = new List<BaseItem>
        {
            TestHelpers.CreateTestItem("Movie 1"),
            TestHelpers.CreateTestItem("Movie 2"),
            TestHelpers.CreateTestItem("Movie 3"),
        };

        _mockProbeService
            .Setup(s => s.GetUnprobedItems(It.IsAny<Guid[]>()))
            .Returns(items);

        _mockProbeService
            .Setup(s => s.ProbeBatchAsync(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProbeResult { Probed = 3, Failed = 0 });

        var progress = new Progress<double>();

        await _task.ExecuteAsync(progress, CancellationToken.None);

        _mockProbeService.Verify(s => s.ProbeBatchAsync(
            It.Is<IReadOnlyList<BaseItem>>(list => list.Count == 3),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<IProgress<double>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PassesConfigValues()
    {
        Plugin.Instance.Configuration.ProbeParallelism = 10;
        Plugin.Instance.Configuration.ProbeTimeoutSeconds = 120;
        Plugin.Instance.Configuration.ProbeCooldownMs = 500;

        var items = new List<BaseItem>
        {
            TestHelpers.CreateTestItem("Movie 1"),
        };

        _mockProbeService
            .Setup(s => s.GetUnprobedItems(It.IsAny<Guid[]>()))
            .Returns(items);

        _mockProbeService
            .Setup(s => s.ProbeBatchAsync(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProbeResult { Probed = 1, Failed = 0 });

        var progress = new Progress<double>();

        await _task.ExecuteAsync(progress, CancellationToken.None);

        _mockProbeService.Verify(s => s.ProbeBatchAsync(
            It.IsAny<IReadOnlyList<BaseItem>>(),
            10,
            120,
            500,
            It.IsAny<IProgress<double>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
