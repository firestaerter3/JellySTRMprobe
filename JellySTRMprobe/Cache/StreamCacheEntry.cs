using System;
using System.Collections.Generic;
using MediaBrowser.Model.Entities;

namespace JellySTRMprobe.Cache;

/// <summary>
/// The stream information of one STRM item, kept outside Jellyfin's database.
/// </summary>
public class StreamCacheEntry
{
    /// <summary>
    /// Gets or sets the item's path when the entry was written.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item's TMDb id, if it has one.
    /// </summary>
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets when the entry was written.
    /// </summary>
    public DateTime SavedUtc { get; set; }

    /// <summary>
    /// Gets or sets the fingerprint of the stored values (see <see cref="StreamCacheStore.Fingerprint"/>).
    /// </summary>
    public string? Fingerprint { get; set; }

    /// <summary>
    /// Gets or sets the run time in ticks.
    /// </summary>
    public long? RunTimeTicks { get; set; }

    /// <summary>
    /// Gets or sets the container.
    /// </summary>
    public string? Container { get; set; }

    /// <summary>
    /// Gets or sets the total bitrate.
    /// </summary>
    public int? TotalBitrate { get; set; }

    /// <summary>
    /// Gets or sets the width of the video.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Gets or sets the height of the video.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the entry was put back into Jellyfin and has not
    /// been confirmed by a real probe since. Such an item gets one real probe on its next start.
    /// </summary>
    public bool NeedsVerification { get; set; }

    /// <summary>
    /// Gets or sets the media streams.
    /// </summary>
    public List<MediaStream> Streams { get; set; } = new();
}
