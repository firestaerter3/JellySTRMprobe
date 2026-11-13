using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using JellySTRMprobe.Cache;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JellySTRMprobe.Tests.Cache;

[Collection("PluginInstance")]
public sealed class StreamCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "jsp-cache-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IMediaStreamRepository> _repo = new();
    private readonly Mock<ILibraryManager> _library = new();
    private readonly StreamCacheService _cache;

    public StreamCacheTests()
    {
        TestHelpers.ResetConfiguration();
        var store = new StreamCacheStore(() => _dir, NullLogger.Instance);
        _cache = new StreamCacheService(_library.Object, _repo.Object, NullLogger<StreamCacheService>.Instance, store);
    }

    public void Dispose()
    {
        _cache.Dispose();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static List<MediaStream> Streams() => new()
    {
        new MediaStream { Type = MediaStreamType.Video, Codec = "hevc", Index = 0, Width = 1920, Height = 800 },
        new MediaStream { Type = MediaStreamType.Audio, Codec = "dts", Index = 1, Language = "eng" },
        new MediaStream { Type = MediaStreamType.Subtitle, Codec = "subrip", Index = 2, Language = "ger" },
    };

    [Fact]
    public async Task SaveThenRestore_AfterTheItemIsRecreated_BringsTheStreamsBackWithoutAProbe()
    {
        var original = TestHelpers.CreateMockItem("Movie A", "/lib/Movies/Movie A [tmdbid-1]/Movie A [tmdbid-1].strm", Streams());
        original.Object.ProviderIds["Tmdb"] = "1";
        original.Object.RunTimeTicks = 123;
        original.Object.Container = "mkv";
        _cache.Save(original.Object).Should().BeTrue();

        var recreatedStreams = new List<MediaStream>();
        var recreated = TestHelpers.CreateMockItem("Movie A", original.Object.Path, recreatedStreams);
        recreated.Object.ProviderIds["Tmdb"] = "1";
        IReadOnlyList<MediaStream>? written = null;
        _repo.Setup(r => r.SaveMediaStreams(recreated.Object.Id, It.IsAny<IReadOnlyList<MediaStream>>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, IReadOnlyList<MediaStream> s, CancellationToken _) => written = s);

        var restored = await _cache.TryRestoreAsync(recreated.Object, CancellationToken.None);

        restored.Should().BeTrue();
        written.Should().NotBeNull();
        written!.Select(s => s.Codec).Should().Equal("hevc", "dts", "subrip");
        recreated.Object.RunTimeTicks.Should().Be(123);
        recreated.Object.Container.Should().Be("mkv");
        recreated.Object.Width.Should().Be(0);
        recreated.Verify(m => m.UpdateToRepositoryAsync(ItemUpdateType.MetadataImport, It.IsAny<CancellationToken>()), Times.Once);
        _cache.NeedsVerification(recreated.Object).Should().BeTrue();
    }

    [Fact]
    public void MarkVerified_ClearsTheFlag()
    {
        var item = TestHelpers.CreateMockItem("Movie B", "/lib/b.strm", Streams()).Object;
        _cache.Save(item);
        var empty = TestHelpers.CreateMockItem("Movie B", "/lib/b.strm", new List<MediaStream>());
        empty.Object.Id = item.Id;
        _cache.TryRestoreAsync(empty.Object, CancellationToken.None).Result.Should().BeTrue();

        _cache.MarkVerified(item);

        _cache.NeedsVerification(item).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAfterRestoreOff_LeavesNoFlag()
    {
        TestHelpers.ResetConfiguration(c => c.VerifyAfterRestore = false);
        _cache.Save(TestHelpers.CreateMockItem("C", "/lib/c.strm", Streams()).Object);
        var empty = TestHelpers.CreateMockItem("C", "/lib/c.strm", new List<MediaStream>()).Object;

        (await _cache.TryRestoreAsync(empty, CancellationToken.None)).Should().BeTrue();

        _cache.NeedsVerification(empty).Should().BeFalse();
    }

    [Fact]
    public async Task DifferentTmdbId_IsADifferentEntry()
    {
        var a = TestHelpers.CreateMockItem("D", "/lib/d.strm", Streams()).Object;
        a.ProviderIds["Tmdb"] = "1";
        _cache.Save(a);
        var b = TestHelpers.CreateMockItem("D", "/lib/d.strm", new List<MediaStream>()).Object;
        b.ProviderIds["Tmdb"] = "2";

        (await _cache.TryRestoreAsync(b, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task ItemWithVideoStream_IsNotOverwritten()
    {
        _cache.Save(TestHelpers.CreateMockItem("E", "/lib/e.strm", Streams()).Object);
        var probed = TestHelpers.CreateMockItem("E", "/lib/e.strm", Streams()).Object;

        (await _cache.TryRestoreAsync(probed, CancellationToken.None)).Should().BeFalse();
        _repo.Verify(r => r.SaveMediaStreams(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<MediaStream>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Save_WithoutVideoStream_WritesNothing()
    {
        var audioOnly = TestHelpers.CreateMockItem("F", "/lib/f.strm", Streams().Where(s => s.Type != MediaStreamType.Video).ToList()).Object;

        _cache.Save(audioOnly).Should().BeFalse();
        Directory.Exists(_dir).Should().BeFalse();
    }

    [Fact]
    public void Save_NonStrm_WritesNothing()
    {
        _cache.Save(TestHelpers.CreateMockItem("G", "/lib/g.mkv", Streams()).Object).Should().BeFalse();
    }

    [Fact]
    public void Save_SameStreamsTwice_WritesOnce()
    {
        var item = TestHelpers.CreateMockItem("H", "/lib/h.strm", Streams()).Object;

        _cache.Save(item).Should().BeTrue();
        _cache.Save(item).Should().BeFalse();
    }

    [Fact]
    public async Task CacheOff_DoesNothing()
    {
        TestHelpers.ResetConfiguration(c => c.EnableStreamCache = false);
        var item = TestHelpers.CreateMockItem("I", "/lib/i.strm", Streams()).Object;

        _cache.Save(item).Should().BeFalse();
        (await _cache.TryRestoreAsync(TestHelpers.CreateMockItem("I", "/lib/i.strm", new List<MediaStream>()).Object, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task DamagedFile_IsIgnored()
    {
        var item = TestHelpers.CreateMockItem("J", "/lib/j.strm", Streams()).Object;
        _cache.Save(item);
        foreach (var f in Directory.GetFiles(_dir, "*.json", SearchOption.AllDirectories))
        {
            await File.WriteAllTextAsync(f, "{ not json");
        }

        (await _cache.TryRestoreAsync(TestHelpers.CreateMockItem("J", "/lib/j.strm", new List<MediaStream>()).Object, CancellationToken.None)).Should().BeFalse();
    }
}
