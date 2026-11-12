using System.Threading;
using System.Threading.Tasks;

namespace JellySTRMprobe.Service;

/// <summary>
/// Tells the probe runs whether someone is playing a STRM item, and waits until nobody is.
/// </summary>
public interface IPlaybackGate
{
    /// <summary>
    /// Returns whether any session is currently playing a STRM item.
    /// </summary>
    /// <returns><c>true</c> when a STRM item is playing.</returns>
    bool IsStrmPlaying();

    /// <summary>
    /// Waits until no session plays a STRM item. Returns at once when nobody does.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when no STRM item is playing.</returns>
    Task WaitUntilNoStrmPlayingAsync(CancellationToken cancellationToken);
}
