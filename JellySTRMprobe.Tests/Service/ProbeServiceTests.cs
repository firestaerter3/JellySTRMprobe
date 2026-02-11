using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using JellySTRMprobe.Service;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JellySTRMprobe.Tests.Service;

public class ProbeServiceTests
{
    private readonly Mock<IProviderManager> _mockProviderManager;
    private readonly Mock<ILibraryManager> _mockLibraryManager;
    private readonly Mock<IFileSystem> _mockFileSystem;
    private readonly Mock<ILogger<ProbeService>> _mockLogger;
    private readonly ProbeService _probeService;

    public ProbeServiceTests()
    {
        _mockProviderManager = new Mock<IProviderManager>();
        _mockLibraryManager = new Mock<ILibraryManager>();
        _mockFileSystem = new Mock<IFileSystem>();
        _mockLogger = new Mock<ILogger<ProbeService>>();

        _probeService = new ProbeService(
            _mockProviderManager.Object,
            _mockLibraryManager.Object,
            _mockFileSystem.Object,
            _mockLogger.Object);
    }

    private static BaseItem CreateMockItem(string name, string? path = null)
    {
        return TestHelpers.CreateTestItem(name, path);
    }

    private void SetupRefreshSuccess()
    {
        _mockProviderManager
            .Setup(p => p.RefreshSingleItem(
                It.IsAny<BaseItem>(),
                It.IsAny<MetadataRefreshOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ItemUpdateType.None);
    }

    // =====================
    // ProbeItemAsync Tests
    // =====================

    [Fact]
    public async Task ProbeItemAsync_SuccessfulProbe_ReturnsTrue()
    {
        var item = CreateMockItem("Test Movie", "/media/test.strm");
        SetupRefreshSuccess();

        var result = await _probeService.ProbeItemAsync(item, 60, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ProbeItemAsync_Timeout_ReturnsFalse()
    {
        var item = CreateMockItem("Slow Movie", "/media/slow.strm");
        _mockProviderManager
            .Setup(p => p.RefreshSingleItem(
                It.IsAny<BaseItem>(),
                It.IsAny<MetadataRefreshOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (BaseItem _, MetadataRefreshOptions _, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                return ItemUpdateType.None;
            });

        var result = await _probeService.ProbeItemAsync(item, 1, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ProbeItemAsync_Exception_ReturnsFalse()
    {
        var item = CreateMockItem("Bad Movie", "/media/bad.strm");
        _mockProviderManager
            .Setup(p => p.RefreshSingleItem(
                It.IsAny<BaseItem>(),
                It.IsAny<MetadataRefreshOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Http.HttpRequestException("Connection refused"));

        var result = await _probeService.ProbeItemAsync(item, 60, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ProbeItemAsync_ParentCancellation_ThrowsOperationCanceled()
    {
        var item = CreateMockItem("Cancelled Movie", "/media/cancel.strm");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockProviderManager
            .Setup(p => p.RefreshSingleItem(
                It.IsAny<BaseItem>(),
                It.IsAny<MetadataRefreshOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (BaseItem _, MetadataRefreshOptions _, CancellationToken ct) =>
            {
                ct.ThrowIfCancellationRequested();
                await Task.CompletedTask.ConfigureAwait(false);
                return ItemUpdateType.None;
            });

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _probeService.ProbeItemAsync(item, 60, cts.Token));
    }

    [Fact]
    public async Task ProbeItemAsync_CorrectRefreshOptions_ArePassedToProvider()
    {
        var item = CreateMockItem("Options Movie", "/media/options.strm");
        MetadataRefreshOptions? capturedOptions = null;

        _mockProviderManager
            .Setup(p => p.RefreshSingleItem(
                It.IsAny<BaseItem>(),
                It.IsAny<MetadataRefreshOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<BaseItem, MetadataRefreshOptions, CancellationToken>((_, options, _) =>
            {
                capturedOptions = options;
            })
            .ReturnsAsync(ItemUpdateType.None);

        await _probeService.ProbeItemAsync(item, 60, CancellationToken.None);

        capturedOptions.Should().NotBeNull();
        capturedOptions!.EnableRemoteContentProbe.Should().BeTrue();
        capturedOptions.ReplaceAllMetadata.Should().BeFalse();
        capturedOptions.ReplaceAllImages.Should().BeFalse();
        capturedOptions.MetadataRefreshMode.Should().Be(MetadataRefreshMode.FullRefresh);
        capturedOptions.ImageRefreshMode.Should().Be(MetadataRefreshMode.ValidationOnly);
    }

    // =====================
    // ProbeBatchAsync Tests
    // =====================

    [Fact]
    public async Task ProbeBatchAsync_AllItemsSucceed_ReturnsCorrectCounts()
    {
        var items = new List<BaseItem>
        {
            CreateMockItem("Movie 1", "/media/1.strm"),
            CreateMockItem("Movie 2", "/media/2.strm"),
            CreateMockItem("Movie 3", "/media/3.strm"),
            CreateMockItem("Movie 4", "/media/4.strm"),
            CreateMockItem("Movie 5", "/media/5.strm"),
        };

        SetupRefreshSuccess();

        var progress = new Progress<double>();

        var result = await _probeService.ProbeBatchAsync(
            items, 5, 60, 0, progress, CancellationToken.None);

        result.Probed.Should().Be(5);
        result.Failed.Should().Be(0);
        result.FailedItems.Should().BeEmpty();
    }

    [Fact]
    public async Task ProbeBatchAsync_MixedResults_ReturnsCorrectCounts()
    {
        var items = new List<BaseItem>
        {
            CreateMockItem("Good 1", "/media/g1.strm"),
            CreateMockItem("Good 2", "/media/g2.strm"),
            CreateMockItem("Good 3", "/media/g3.strm"),
            CreateMockItem("Bad 1", "/media/b1.strm"),
            CreateMockItem("Bad 2", "/media/b2.strm"),
        };

        var callCount = 0;
        _mockProviderManager
            .Setup(p => p.RefreshSingleItem(
                It.IsAny<BaseItem>(),
                It.IsAny<MetadataRefreshOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var current = Interlocked.Increment(ref callCount);
                if (current > 3)
                {
                    throw new System.Net.Http.HttpRequestException("Failed");
                }

                return Task.FromResult(ItemUpdateType.None);
            });

        var progress = new Progress<double>();

        var result = await _probeService.ProbeBatchAsync(
            items, 1, 60, 0, progress, CancellationToken.None);

        result.Probed.Should().Be(3);
        result.Failed.Should().Be(2);
        result.FailedItems.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProbeBatchAsync_RespectsParallelismLimit()
    {
        var maxConcurrent = 0;
        var currentConcurrent = 0;
        var items = new List<BaseItem>();
        for (int i = 0; i < 10; i++)
        {
            items.Add(CreateMockItem($"Movie {i}", $"/media/{i}.strm"));
        }

        _mockProviderManager
            .Setup(p => p.RefreshSingleItem(
                It.IsAny<BaseItem>(),
                It.IsAny<MetadataRefreshOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var concurrent = Interlocked.Increment(ref currentConcurrent);
                var currentMax = maxConcurrent;
                while (concurrent > currentMax)
                {
                    currentMax = Interlocked.CompareExchange(ref maxConcurrent, concurrent, currentMax);
                }

                await Task.Delay(50).ConfigureAwait(false);
                Interlocked.Decrement(ref currentConcurrent);
                return ItemUpdateType.None;
            });

        var progress = new Progress<double>();

        await _probeService.ProbeBatchAsync(
            items, 2, 60, 0, progress, CancellationToken.None);

        maxConcurrent.Should().BeLessOrEqualTo(2);
    }

    [Fact]
    public async Task ProbeBatchAsync_ReportsProgress()
    {
        var items = new List<BaseItem>
        {
            CreateMockItem("Movie 1", "/media/1.strm"),
            CreateMockItem("Movie 2", "/media/2.strm"),
            CreateMockItem("Movie 3", "/media/3.strm"),
            CreateMockItem("Movie 4", "/media/4.strm"),
        };

        SetupRefreshSuccess();

        var progressValues = new List<double>();
        var progress = new Progress<double>(v => progressValues.Add(v));

        await _probeService.ProbeBatchAsync(
            items, 1, 60, 0, progress, CancellationToken.None);

        // Wait for Progress<T> callbacks (they are posted to the thread pool)
        await Task.Delay(200);

        progressValues.Should().NotBeEmpty();
        progressValues.Should().HaveCountGreaterOrEqualTo(1);
        progressValues.Should().OnlyContain(v => v > 0 && v <= 100);
    }

    [Fact]
    public async Task ProbeBatchAsync_AppliesCooldown()
    {
        var items = new List<BaseItem>
        {
            CreateMockItem("Movie 1", "/media/1.strm"),
            CreateMockItem("Movie 2", "/media/2.strm"),
            CreateMockItem("Movie 3", "/media/3.strm"),
        };

        SetupRefreshSuccess();

        var progress = new Progress<double>();
        var sw = Stopwatch.StartNew();

        await _probeService.ProbeBatchAsync(
            items, 1, 60, 100, progress, CancellationToken.None);

        sw.Stop();

        // With parallelism=1 and cooldown=100ms, 3 items should take at least 300ms
        sw.ElapsedMilliseconds.Should().BeGreaterOrEqualTo(250); // Allow some tolerance
    }

    [Fact]
    public async Task ProbeBatchAsync_CancellationStopsProcessing()
    {
        var items = new List<BaseItem>();
        for (int i = 0; i < 10; i++)
        {
            items.Add(CreateMockItem($"Movie {i}", $"/media/{i}.strm"));
        }

        var cts = new CancellationTokenSource();
        var processedCount = 0;

        _mockProviderManager
            .Setup(p => p.RefreshSingleItem(
                It.IsAny<BaseItem>(),
                It.IsAny<MetadataRefreshOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (BaseItem _, MetadataRefreshOptions _, CancellationToken ct) =>
            {
                var count = Interlocked.Increment(ref processedCount);
                if (count >= 2)
                {
                    cts.Cancel();
                }

                await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
                return ItemUpdateType.None;
            });

        var progress = new Progress<double>();

        // Should throw OperationCanceledException or complete with partial results
        try
        {
            await _probeService.ProbeBatchAsync(
                items, 1, 60, 50, progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        processedCount.Should().BeLessThan(10);
    }

    [Fact]
    public async Task ProbeBatchAsync_EmptyList_ReturnsZeroCounts()
    {
        var items = new List<BaseItem>();
        var progress = new Progress<double>();

        var result = await _probeService.ProbeBatchAsync(
            items, 5, 60, 0, progress, CancellationToken.None);

        result.Probed.Should().Be(0);
        result.Failed.Should().Be(0);
        result.FailedItems.Should().BeEmpty();
    }

    // =====================
    // GetUnprobedItems Tests
    // =====================

    private void SetupItemIds(params BaseItem[] items)
    {
        var ids = new List<Guid>();
        foreach (var item in items)
        {
            ids.Add(item.Id);
            _mockLibraryManager
                .Setup(l => l.GetItemById(item.Id))
                .Returns(item);
        }

        _mockLibraryManager
            .Setup(l => l.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Returns(ids);
    }

    [Fact]
    public void GetUnprobedItems_CallsLibraryManager_WithGetItemIds()
    {
        _mockLibraryManager
            .Setup(l => l.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<Guid>());

        _probeService.GetUnprobedItems(Array.Empty<Guid>());

        _mockLibraryManager.Verify(l => l.GetItemIds(It.IsAny<InternalItemsQuery>()), Times.Once);
    }

    [Fact]
    public void GetUnprobedItems_WithLibraryIds_SetsAncestorIds()
    {
        var libraryId = Guid.NewGuid();
        _mockLibraryManager
            .Setup(l => l.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<Guid>());

        _probeService.GetUnprobedItems(new[] { libraryId });

        _mockLibraryManager.Verify(l => l.GetItemIds(It.Is<InternalItemsQuery>(q =>
            q.AncestorIds != null &&
            q.AncestorIds.Length == 1 &&
            q.AncestorIds[0] == libraryId)), Times.Once);
    }

    [Fact]
    public void GetUnprobedItems_WithEmptyLibraryIds_DoesNotSetAncestorIds()
    {
        _mockLibraryManager
            .Setup(l => l.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<Guid>());

        _probeService.GetUnprobedItems(Array.Empty<Guid>());

        _mockLibraryManager.Verify(l => l.GetItemIds(It.Is<InternalItemsQuery>(q =>
            q.AncestorIds == null || q.AncestorIds.Length == 0)), Times.Once);
    }

    [Fact]
    public void GetUnprobedItems_FiltersStrmPaths()
    {
        var strmItem = CreateMockItem("STRM Movie", "/media/movie.strm");
        var mkvItem = CreateMockItem("MKV Movie", "/media/movie.mkv");
        var nullPathItem = CreateMockItem("No Path");

        SetupItemIds(strmItem, mkvItem, nullPathItem);

        var result = _probeService.GetUnprobedItems(Array.Empty<Guid>());

        result.Should().HaveCount(1);
        result[0].Path.Should().EndWith(".strm");
    }

    [Fact]
    public void GetUnprobedItems_CaseInsensitiveStrmExtension()
    {
        var upperItem = CreateMockItem("Upper STRM", "/media/movie.STRM");
        var mixedItem = CreateMockItem("Mixed STRM", "/media/movie.Strm");

        SetupItemIds(upperItem, mixedItem);

        var result = _probeService.GetUnprobedItems(Array.Empty<Guid>());

        result.Should().HaveCount(2);
    }

    [Fact]
    public void GetUnprobedItems_HandlesNullPathGracefully()
    {
        var nullPathItem = CreateMockItem("No Path");

        SetupItemIds(nullPathItem);

        var result = _probeService.GetUnprobedItems(Array.Empty<Guid>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetUnprobedItems_ReturnsEmptyWhenNoResults()
    {
        _mockLibraryManager
            .Setup(l => l.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<Guid>());

        var result = _probeService.GetUnprobedItems(Array.Empty<Guid>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetUnprobedItems_SkipsUnresolvableItems()
    {
        var goodItem = CreateMockItem("Good STRM", "/media/good.strm");
        var badId = Guid.NewGuid();

        _mockLibraryManager
            .Setup(l => l.GetItemIds(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<Guid> { goodItem.Id, badId });
        _mockLibraryManager
            .Setup(l => l.GetItemById(goodItem.Id))
            .Returns(goodItem);
        _mockLibraryManager
            .Setup(l => l.GetItemById(badId))
            .Throws(new InvalidOperationException("Cannot deserialize unknown type"));

        var result = _probeService.GetUnprobedItems(Array.Empty<Guid>());

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Good STRM");
    }

    // =====================
    // DeleteStrmFiles Tests
    // =====================

    [Fact]
    public void DeleteStrmFiles_DeletesExistingStrmFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jellystrm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var strmPath = Path.Combine(tempDir, "movie.strm");
            File.WriteAllText(strmPath, "http://example.com/stream.mkv");

            var item = CreateMockItem("Test Movie", strmPath);
            var items = new List<BaseItem> { item };

            var deleted = _probeService.DeleteStrmFiles(items);

            deleted.Should().Be(1);
            File.Exists(strmPath).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DeleteStrmFiles_SkipsNonStrmPaths()
    {
        var item = CreateMockItem("MKV Movie", "/media/movie.mkv");
        var items = new List<BaseItem> { item };

        var deleted = _probeService.DeleteStrmFiles(items);

        deleted.Should().Be(0);
    }

    [Fact]
    public void DeleteStrmFiles_SkipsNullPaths()
    {
        var item = CreateMockItem("No Path");
        var items = new List<BaseItem> { item };

        var deleted = _probeService.DeleteStrmFiles(items);

        deleted.Should().Be(0);
    }

    [Fact]
    public void DeleteStrmFiles_HandlesNonExistentFileGracefully()
    {
        var item = CreateMockItem("Missing STRM", "/nonexistent/path/movie.strm");
        var items = new List<BaseItem> { item };

        // Non-existent directory causes DirectoryNotFoundException, caught gracefully
        var deleted = _probeService.DeleteStrmFiles(items);

        deleted.Should().Be(0);
    }

    [Fact]
    public void DeleteStrmFiles_ReturnsCorrectCountForMixedItems()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jellystrm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var strmPath1 = Path.Combine(tempDir, "movie1.strm");
            var strmPath2 = Path.Combine(tempDir, "movie2.strm");
            File.WriteAllText(strmPath1, "http://example.com/1.mkv");
            File.WriteAllText(strmPath2, "http://example.com/2.mkv");

            var items = new List<BaseItem>
            {
                CreateMockItem("Movie 1", strmPath1),
                CreateMockItem("Not STRM", "/media/movie.mkv"),
                CreateMockItem("Movie 2", strmPath2),
                CreateMockItem("No Path"),
            };

            var deleted = _probeService.DeleteStrmFiles(items);

            deleted.Should().Be(2);
            File.Exists(strmPath1).Should().BeFalse();
            File.Exists(strmPath2).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
