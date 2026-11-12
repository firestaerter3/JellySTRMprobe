using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace JellySTRMprobe.Service;

/// <summary>
/// Uses the session manager to find out whether someone is playing a STRM item.
/// </summary>
public class PlaybackGate : IPlaybackGate
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly ISessionManager _sessionManager;
    private readonly ILogger<PlaybackGate> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackGate"/> class.
    /// </summary>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{PlaybackGate}"/> interface.</param>
    public PlaybackGate(ISessionManager sessionManager, ILogger<PlaybackGate> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsStrmPlaying()
    {
        // A session whose item is unknown counts as playing a STRM item: waiting is cheap,
        // a probe next to a viewer on a single-connection provider breaks one of the two.
        return _sessionManager.Sessions.Any(session =>
            session.NowPlayingItem != null
            && (session.FullNowPlayingItem == null || LibraryScope.IsStrm(session.FullNowPlayingItem)));
    }

    /// <inheritdoc />
    public async Task WaitUntilNoStrmPlayingAsync(CancellationToken cancellationToken)
    {
        var logged = false;
        while (IsStrmPlaying())
        {
            if (!logged)
            {
                _logger.LogInformation("A STRM item is playing; probing waits until playback stops");
                logged = true;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        if (logged)
        {
            _logger.LogInformation("No STRM item is playing anymore; probing continues");
        }
    }
}
