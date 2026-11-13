using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace JellySTRMprobe.Cache;

/// <summary>
/// A copy of the stream information of probed STRM items, kept outside Jellyfin's database.
/// </summary>
public interface IStreamCache
{
    /// <summary>
    /// Writes the item's current streams to the cache when it has a video stream and the entry differs.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns><c>true</c> when an entry was written.</returns>
    bool Save(BaseItem item);

    /// <summary>
    /// Puts the cached streams back into Jellyfin when the item has no video stream and the cache has one.
    /// Makes no request to the upstream server.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the item was restored.</returns>
    Task<bool> TryRestoreAsync(BaseItem item, CancellationToken cancellationToken);

    /// <summary>
    /// Returns whether the item's streams were restored from the cache and not yet confirmed by a real probe.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns><c>true</c> when the next start should probe for real.</returns>
    bool NeedsVerification(BaseItem item);

    /// <summary>
    /// Registers that a real probe of the item is about to run, until the returned handle is
    /// disposed. A restore never writes while such a probe is registered, and a probe never
    /// registers while a restore is writing, so a fresh probe result is never overwritten.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>A handle that ends the registration.</returns>
    IDisposable BeginProbe(Guid itemId);

    /// <summary>
    /// Records that a real probe confirmed the item's streams.
    /// </summary>
    /// <param name="item">The item.</param>
    void MarkVerified(BaseItem item);
}
