using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using JellySTRMprobe.Service;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JellySTRMprobe.Tests.Service;

public class PlaybackGateTests
{
    private readonly Mock<ISessionManager> _sessionManager = new();

    private SessionInfo Session(string? playingPath, bool playing = true)
    {
        var session = new SessionInfo(_sessionManager.Object, NullLogger.Instance);
        if (playing)
        {
            session.NowPlayingItem = new BaseItemDto { Name = "x" };
            session.FullNowPlayingItem = playingPath == null ? null! : TestHelpers.CreateTestItem("x", playingPath);
        }

        return session;
    }

    private PlaybackGate Gate(params SessionInfo[] sessions)
    {
        _sessionManager.Setup(m => m.Sessions).Returns(new List<SessionInfo>(sessions));
        return new PlaybackGate(_sessionManager.Object, NullLogger<PlaybackGate>.Instance);
    }

    [Fact]
    public void NoSessions_NothingPlaying()
    {
        Gate().IsStrmPlaying().Should().BeFalse();
    }

    [Fact]
    public void IdleSession_NothingPlaying()
    {
        Gate(Session(null, playing: false)).IsStrmPlaying().Should().BeFalse();
    }

    [Fact]
    public void StrmPlaying_IsDetected()
    {
        Gate(Session("/x/Movies/a.strm")).IsStrmPlaying().Should().BeTrue();
    }

    [Fact]
    public void LocalFilePlaying_IsNotAStrm()
    {
        Gate(Session("/x/Movies/a.mkv")).IsStrmPlaying().Should().BeFalse();
    }

    [Fact]
    public void UnknownItemPlaying_CountsAsStrm()
    {
        Gate(Session(null)).IsStrmPlaying().Should().BeTrue();
    }

    [Fact]
    public async Task Wait_NothingPlaying_ReturnsAtOnce()
    {
        var task = Gate().WaitUntilNoStrmPlayingAsync(CancellationToken.None);

        await task;
        task.IsCompletedSuccessfully.Should().BeTrue();
    }
}
