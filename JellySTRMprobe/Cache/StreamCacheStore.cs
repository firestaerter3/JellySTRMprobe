using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace JellySTRMprobe.Cache;

/// <summary>
/// Stores <see cref="StreamCacheEntry"/> files, one JSON file per item, in a directory.
/// </summary>
/// <remarks>
/// The key is the item's path plus its TMDb id. A STRM generator typically writes the same path
/// (<c>Movies/Title [tmdbid-N]/Title [tmdbid-N].strm</c>) when it recreates an item, even if the
/// URL inside the file changes. One file per item keeps a lookup cheap and a damaged file
/// limited to one item.
/// </remarks>
public class StreamCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly Func<string> _directory;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamCacheStore"/> class.
    /// </summary>
    /// <param name="directory">Returns the directory for the cache files.</param>
    /// <param name="logger">Logger.</param>
    public StreamCacheStore(Func<string> directory, ILogger logger)
    {
        _directory = directory;
        _logger = logger;
    }

    /// <summary>
    /// Builds the cache key of an item.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The key, or <c>null</c> when the item has no path.</returns>
    public static string? KeyOf(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrEmpty(item.Path))
        {
            return null;
        }

        item.ProviderIds.TryGetValue(MetadataProvider.Tmdb.ToString(), out var tmdb);
        return item.Path.ToUpperInvariant() + "|" + (tmdb ?? string.Empty);
    }

    /// <summary>
    /// Reads the entry of an item.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The entry, or <c>null</c> when there is none or it cannot be read.</returns>
    public StreamCacheEntry? Get(BaseItem item)
    {
        var file = FileOf(item);
        if (file == null || !File.Exists(file))
        {
            return null;
        }

        try
        {
            var entry = JsonSerializer.Deserialize<StreamCacheEntry>(File.ReadAllText(file), JsonOptions);
            return entry is { Streams.Count: > 0 } ? entry : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Stream cache: cannot read the entry of {ItemId}; ignoring it", item.Id);
            return null;
        }
    }

    /// <summary>
    /// Writes the entry of an item. Writes to a temporary file first, so a crash never leaves a half file.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="entry">The entry.</param>
    public void Put(BaseItem item, StreamCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var file = FileOf(item);
        if (file == null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entry, JsonOptions));
        File.Move(temp, file, true);
    }

    private string? FileOf(BaseItem item)
    {
        var key = KeyOf(item);
        if (key == null)
        {
            return null;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_directory(), hash[..2], hash + ".json");
    }
}
