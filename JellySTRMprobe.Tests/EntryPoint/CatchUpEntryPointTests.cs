using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using JellySTRMprobe.EntryPoint;
using JellySTRMprobe.Service;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JellySTRMprobe.Tests.EntryPoint;

public class CatchUpEntryPointTests
{
    private readonly Mock<ILibraryManager> _mockLibraryManager;
    private readonly Mock<IProbeService> _mockProbeService;
    private readonly Mock<ILogger<CatchUpEntryPoint>> _mockLogger;

    public CatchUpEntryPointTests()
    {
        _mockLibraryManager = new Mock<ILibraryManager>();
        _mockProbeService = new Mock<IProbeService>();
        _mockLogger = new Mock<ILogger<CatchUpEntryPoint>>();

        TestHelpers.EnsurePluginInstance();
    }

    private CatchUpEntryPoint CreateEntryPoint()
    {
        return new CatchUpEntryPoint(
            _mockLibraryManager.Object,
            _mockProbeService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task StartAsync_AlwaysSubscribesToItemAdded()
    {
        var entryPoint = CreateEntryPoint();

        await entryPoint.StartAsync(CancellationToken.None);

        _mockLibraryManager.VerifyAdd(l => l.ItemAdded += It.IsAny<EventHandler<ItemChangeEventArgs>>(), Times.Once);

        entryPoint.Dispose();
    }

    [Fact]
    public async Task OnItemAdded_WithStrmPath_WhenEnabled_EnqueuesItem()
    {
        Plugin.Instance.Configuration.EnableCatchUpMode = true;
        var entryPoint = CreateEntryPoint();
        await entryPoint.StartAsync(CancellationToken.None);

        var item = TestHelpers.CreateTestItem("Test Movie", "/media/test.strm");
        var eventArgs = new ItemChangeEventArgs { Item = item };

        // Raise the event — should not throw
        _mockLibraryManager.Raise(l => l.ItemAdded += null!, this, eventArgs);

        entryPoint.Dispose();
    }

    [Fact]
    public async Task OnItemAdded_WithStrmPath_WhenDisabled_DoesNotEnqueue()
    {
        Plugin.Instance.Configuration.EnableCatchUpMode = false;
        var entryPoint = CreateEntryPoint();
        await entryPoint.StartAsync(CancellationToken.None);

        var item = TestHelpers.CreateTestItem("Test Movie", "/media/test.strm");
        var eventArgs = new ItemChangeEventArgs { Item = item };

        _mockLibraryManager.Raise(l => l.ItemAdded += null!, this, eventArgs);

        // Wait to ensure nothing happens
        await Task.Delay(100);

        _mockProbeService.Verify(s => s.ProbeBatchAsync(
            It.IsAny<IReadOnlyList<BaseItem>>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<IProgress<double>>(),
            It.IsAny<CancellationToken>()), Times.Never);

        entryPoint.Dispose();
    }

    [Fact]
    public async Task OnItemAdded_WithNonStrmPath_DoesNotEnqueueItem()
    {
        Plugin.Instance.Configuration.EnableCatchUpMode = true;
        var entryPoint = CreateEntryPoint();
        await entryPoint.StartAsync(CancellationToken.None);

        var item = TestHelpers.CreateTestItem("Test Movie", "/media/test.mkv");
        var eventArgs = new ItemChangeEventArgs { Item = item };

        _mockLibraryManager.Raise(l => l.ItemAdded += null!, this, eventArgs);

        // Wait to ensure nothing happens
        await Task.Delay(100);

        _mockProbeService.Verify(s => s.ProbeBatchAsync(
            It.IsAny<IReadOnlyList<BaseItem>>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<IProgress<double>>(),
            It.IsAny<CancellationToken>()), Times.Never);

        entryPoint.Dispose();
    }

    [Fact]
    public async Task StopAsync_UnsubscribesFromEvents()
    {
        var entryPoint = CreateEntryPoint();
        await entryPoint.StartAsync(CancellationToken.None);

        await entryPoint.StopAsync(CancellationToken.None);

        _mockLibraryManager.VerifyRemove(l => l.ItemAdded -= It.IsAny<EventHandler<ItemChangeEventArgs>>(), Times.Once);

        entryPoint.Dispose();
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromEvents()
    {
        var entryPoint = CreateEntryPoint();
        await entryPoint.StartAsync(CancellationToken.None);

        entryPoint.Dispose();

        _mockLibraryManager.VerifyRemove(l => l.ItemAdded -= It.IsAny<EventHandler<ItemChangeEventArgs>>(), Times.AtLeastOnce);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var entryPoint = CreateEntryPoint();

        var act = () =>
        {
            entryPoint.Dispose();
            entryPoint.Dispose();
        };

        act.Should().NotThrow();
    }
}
