using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace JellySTRMprobe.Cache;

/// <summary>
/// Remembers which items failed a probe and when, in one JSON file, so a scheduled run can
/// leave them alone for a number of days instead of failing on them again every night.
/// </summary>
/// <remarks>
/// The key is the same as for the stream cache (path plus TMDb id), so an item that is
/// recreated at the same path stays skipped. The file is small (one line per failed item) and is
/// written to a temporary file first, so a crash never leaves a half file. A missing or damaged
/// file means "nothing is skipped".
/// </remarks>
public class FailedProbeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly object _lock = new();
    private readonly Func<string> _file;
    private readonly Func<DateTime> _utcNow;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FailedProbeStore"/> class.
    /// </summary>
    /// <param name="file">Returns the path of the JSON file.</param>
    /// <param name="utcNow">Returns the current UTC time.</param>
    /// <param name="logger">Logger.</param>
    public FailedProbeStore(Func<string> file, Func<DateTime> utcNow, ILogger logger)
    {
        _file = file;
        _utcNow = utcNow;
        _logger = logger;
    }

    /// <summary>
    /// Returns the items that failed less than <paramref name="skipDays"/> days ago.
    /// </summary>
    /// <param name="items">The candidate items.</param>
    /// <param name="skipDays">Days to skip a failed item; 0 or less skips nothing.</param>
    /// <returns>The items that should not be probed now.</returns>
    public HashSet<Guid> RecentlyFailed(IEnumerable<BaseItem> items, int skipDays)
    {
        ArgumentNullException.ThrowIfNull(items);
        var result = new HashSet<Guid>();
        if (skipDays <= 0)
        {
            return result;
        }

        var limit = _utcNow().AddDays(-skipDays);
        Dictionary<string, DateTime> entries;
        lock (_lock)
        {
            entries = Load();
        }

        foreach (var item in items)
        {
            var key = StreamCacheStore.KeyOf(item);
            if (key != null && entries.TryGetValue(key, out var failedAt) && failedAt > limit)
            {
                result.Add(item.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// Records that these items failed now, and drops entries older than <paramref name="keepDays"/> days.
    /// </summary>
    /// <param name="failed">The items that failed.</param>
    /// <param name="keepDays">Entries older than this are removed.</param>
    public void Record(IEnumerable<BaseItem> failed, int keepDays)
    {
        ArgumentNullException.ThrowIfNull(failed);
        var now = _utcNow();
        lock (_lock)
        {
            var entries = Load();
            var oldest = now.AddDays(-Math.Max(1, keepDays));
            foreach (var stale in entries.Where(e => e.Value <= oldest).Select(e => e.Key).ToList())
            {
                entries.Remove(stale);
            }

            foreach (var item in failed)
            {
                var key = StreamCacheStore.KeyOf(item);
                if (key != null)
                {
                    entries[key] = now;
                }
            }

            Save(entries);
        }
    }

    private Dictionary<string, DateTime> Load()
    {
        var file = _file();
        if (!File.Exists(file))
        {
            return new Dictionary<string, DateTime>(StringComparer.Ordinal);
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(file), JsonOptions);
            return loaded == null
                ? new Dictionary<string, DateTime>(StringComparer.Ordinal)
                : new Dictionary<string, DateTime>(loaded, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed-probe list: cannot read {File}; nothing is skipped", file);
            return new Dictionary<string, DateTime>(StringComparer.Ordinal);
        }
    }

    private void Save(Dictionary<string, DateTime> entries)
    {
        var file = _file();
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(entries, JsonOptions));
            File.Move(temp, file, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed-probe list: cannot write {File}", file);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
