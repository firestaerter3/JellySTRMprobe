using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Jellyfin.Database.Implementations.Entities;
using JellySTRMprobe.Playback;
using JellySTRMprobe.Service;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JellySTRMprobe.Tests.Playback;

public class FastStartMediaSourceManagerTests
{
    private static readonly MediaStream Video = new() { Type = MediaStreamType.Video, Codec = "h264" };
    private static readonly MediaStream Audio = new() { Type = MediaStreamType.Audio, Codec = "aac" };

    private static bool AnyLibrary(BaseItem item) => true;

    private static PluginConfiguration Config(bool fastStart = true) => new() { EnableFastStart = fastStart };

    [Fact]
    public void ShouldSkipProbe_ProbedStrm_Skips()
    {
        var item = TestHelpers.CreateTestItemWithStreams("2 Guns", "/x/Movies/a.strm", Video, Audio);

        FastStartMediaSourceManager.ShouldSkipProbe(item, true, Config(), AnyLibrary).Should().BeTrue();
    }

    [Fact]
    public void ShouldSkipProbe_StrmWithoutStreams_DoesNotSkip()
    {
        var item = TestHelpers.CreateTestItemWithStreams("Never played", "/x/Movies/b.strm");

        FastStartMediaSourceManager.ShouldSkipProbe(item, true, Config(), AnyLibrary).Should().BeFalse();
    }

    [Fact]
    public void ShouldSkipProbe_StrmWithAudioOnly_DoesNotSkip()
    {
        var item = TestHelpers.CreateTestItemWithStreams("Audio only", "/x/Movies/c.strm", Audio);

        FastStartMediaSourceManager.ShouldSkipProbe(item, true, Config(), AnyLibrary).Should().BeFalse();
    }

    [Fact]
    public void ShouldSkipProbe_LocalFile_DoesNotSkip()
    {
        var item = TestHelpers.CreateTestItemWithStreams("Local", "/x/Movies/d.mkv", Video, Audio);

        FastStartMediaSourceManager.ShouldSkipProbe(item, true, Config(), AnyLibrary).Should().BeFalse();
    }

    [Fact]
    public void ShouldSkipProbe_Disabled_DoesNotSkip()
    {
        var item = TestHelpers.CreateTestItemWithStreams("2 Guns", "/x/Movies/a.strm", Video, Audio);

        FastStartMediaSourceManager.ShouldSkipProbe(item, true, Config(fastStart: false), AnyLibrary).Should().BeFalse();
    }

    [Fact]
    public void ShouldSkipProbe_NoConfiguration_DoesNotSkip()
    {
        var item = TestHelpers.CreateTestItemWithStreams("2 Guns", "/x/Movies/a.strm", Video, Audio);

        FastStartMediaSourceManager.ShouldSkipProbe(item, true, null, AnyLibrary).Should().BeFalse();
    }

    [Fact]
    public void ShouldSkipProbe_CallerAlreadyDisallowsProbe_ReturnsFalse()
    {
        var item = TestHelpers.CreateTestItemWithStreams("2 Guns", "/x/Movies/a.strm", Video, Audio);

        FastStartMediaSourceManager.ShouldSkipProbe(item, false, Config(), AnyLibrary).Should().BeFalse();
    }

    [Fact]
    public void ShouldSkipProbe_OutsideSelectedLibraries_DoesNotSkip()
    {
        var item = TestHelpers.CreateTestItemWithStreams("2 Guns", "/x/Movies/a.strm", Video, Audio);

        FastStartMediaSourceManager.ShouldSkipProbe(item, true, Config(), _ => false).Should().BeFalse();
    }

    [Fact]
    public async Task GetPlaybackMediaSources_ProbedStrm_PassesNoProbeToInner()
    {
        var inner = new Mock<IMediaSourceManager>();
        inner.Setup(m => m.GetPlaybackMediaSources(It.IsAny<BaseItem>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MediaSourceInfo>());
        var sut = Create(inner.Object, Config());
        var item = TestHelpers.CreateTestItemWithStreams("2 Guns", "/x/Movies/a.strm", Video, Audio);

        await sut.GetPlaybackMediaSources(item, null!, true, true, CancellationToken.None);

        inner.Verify(m => m.GetPlaybackMediaSources(item, null!, false, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPlaybackMediaSources_UnprobedStrm_KeepsProbe()
    {
        var inner = new Mock<IMediaSourceManager>();
        inner.Setup(m => m.GetPlaybackMediaSources(It.IsAny<BaseItem>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MediaSourceInfo>());
        var sut = Create(inner.Object, Config());
        var item = TestHelpers.CreateTestItemWithStreams("Never played", "/x/Movies/b.strm");

        await sut.GetPlaybackMediaSources(item, null!, true, true, CancellationToken.None);

        inner.Verify(m => m.GetPlaybackMediaSources(item, null!, true, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPlaybackMediaSources_CheckThrows_FallsBackToProbe()
    {
        var inner = new Mock<IMediaSourceManager>();
        inner.Setup(m => m.GetPlaybackMediaSources(It.IsAny<BaseItem>(), It.IsAny<User>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MediaSourceInfo>());
        var sut = Create(inner.Object, Config());
        var broken = new Mock<BaseItem>() { CallBase = true };
        broken.Setup(m => m.GetMediaStreams()).Throws(new InvalidOperationException("db gone"));
        broken.Object.Path = "/x/Movies/e.strm";

        await sut.GetPlaybackMediaSources(broken.Object, null!, true, true, CancellationToken.None);

        inner.Verify(m => m.GetPlaybackMediaSources(broken.Object, null!, true, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void OtherMethods_AreForwardedToInner()
    {
        var inner = new Mock<IMediaSourceManager>();
        var id = Guid.NewGuid();
        inner.Setup(m => m.GetMediaStreams(id)).Returns(new List<MediaStream> { Video });
        var sut = Create(inner.Object, Config());

        sut.GetMediaStreams(id).Should().ContainSingle();
        sut.AddParts(Array.Empty<IMediaSourceProvider>());

        inner.Verify(m => m.GetMediaStreams(id), Times.Once);
        inner.Verify(m => m.AddParts(It.IsAny<IEnumerable<IMediaSourceProvider>>()), Times.Once);
    }

    [Fact]
    public void Dispose_OwnedInner_IsDisposed()
    {
        var inner = new Mock<IMediaSourceManager>();
        var disposable = inner.As<IDisposable>();
        var sut = new FastStartMediaSourceManager(inner.Object, true, new ServiceCollection().BuildServiceProvider(), NullLogger<FastStartMediaSourceManager>.Instance, () => Config());

        sut.Dispose();
        sut.Dispose();

        disposable.Verify(d => d.Dispose(), Times.Once);
    }

    [Fact]
    public void Dispose_SharedInner_IsNotDisposed()
    {
        var inner = new Mock<IMediaSourceManager>();
        var disposable = inner.As<IDisposable>();
        var sut = new FastStartMediaSourceManager(inner.Object, false, new ServiceCollection().BuildServiceProvider(), NullLogger<FastStartMediaSourceManager>.Instance, () => Config());

        sut.Dispose();

        disposable.Verify(d => d.Dispose(), Times.Never);
    }

    [Fact]
    public void LibraryScope_SelectedLibrary_MatchesCollectionFolder()
    {
        var libraryId = Guid.NewGuid();
        var libraryManager = new Mock<ILibraryManager>();
        var item = TestHelpers.CreateTestItem("2 Guns", "/x/Movies/a.strm");
        libraryManager.Setup(l => l.GetCollectionFolders(item)).Returns(new List<Folder> { new Folder { Id = libraryId } });

        LibraryScope.IsInSelectedLibraries(libraryManager.Object, item, new[] { libraryId }).Should().BeTrue();
        LibraryScope.IsInSelectedLibraries(libraryManager.Object, item, new[] { Guid.NewGuid() }).Should().BeFalse();
        LibraryScope.IsInSelectedLibraries(libraryManager.Object, item, Array.Empty<Guid>()).Should().BeTrue();
    }

    [Fact]
    public void Registrator_WrapsTheServerRegistration()
    {
        var original = new Mock<IMediaSourceManager>().Object;
        var services = new ServiceCollection();
        services.AddSingleton(original);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        PluginServiceRegistrator.DecorateMediaSourceManager(services);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMediaSourceManager>().Should().BeOfType<FastStartMediaSourceManager>();
    }

    [Fact]
    public void Registrator_WithoutServerRegistration_AddsNothing()
    {
        var services = new ServiceCollection();

        PluginServiceRegistrator.DecorateMediaSourceManager(services);

        services.Should().BeEmpty();
    }

    private static FastStartMediaSourceManager Create(IMediaSourceManager inner, PluginConfiguration config)
    {
        return new FastStartMediaSourceManager(
            inner,
            false,
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<FastStartMediaSourceManager>.Instance,
            () => config);
    }
}
