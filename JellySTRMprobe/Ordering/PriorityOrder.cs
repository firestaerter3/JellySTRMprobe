using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;

namespace JellySTRMprobe.Ordering;

/// <summary>
/// Orders unprobed items so the ones most likely to be watched are probed first.
/// </summary>
/// <remarks>
/// Three groups, in this order: items that match a preferred language, country, tag or title
/// prefix; other items; items under the minimum rating. Within a group the score is
/// <c>RatingWeight × rating + RecencyWeight × recency</c>, where an unknown rating counts as 5 and
/// recency runs from 1 (this year) to 0 (ten years or older), plus half a point of RecencyWeight
/// for items added in the last 30 days. Scores are cut into bands of
/// <c>OrderRandomnessBand</c> points; inside a band the order is random, so runs do not walk the
/// catalogue in a fixed sequence. A band of 0 gives a strict order.
/// </remarks>
public static class PriorityOrder
{
    /// <summary>
    /// Orders the items.
    /// </summary>
    /// <param name="items">The items.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="random">Random source for the order inside a band.</param>
    /// <returns>The items, highest priority first.</returns>
    public static IReadOnlyList<BaseItem> Sort(IReadOnlyList<BaseItem> items, PluginConfiguration config, DateTime nowUtc, Random random)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(random);

        var languages = Split(config.PriorityLanguages);
        var countries = Split(config.PriorityCountries);
        var tags = Split(config.PriorityTags);
        var prefixes = Split(config.PriorityTitlePrefixes);
        var band = config.OrderRandomnessBand;

        return items
            .Select(item => (
                Item: item,
                Group: GroupOf(item, config.MinimumRating, languages, countries, tags, prefixes),
                Score: Score(item, config.RatingWeight, config.RecencyWeight, nowUtc),
                Tiebreak: random.Next()))
            .OrderBy(x => x.Group)
            .ThenByDescending(x => band > 0 ? Math.Floor(x.Score / band) : x.Score)
            .ThenBy(x => x.Tiebreak)
            .Select(x => x.Item)
            .ToList();
    }

    /// <summary>
    /// Returns the item's group: 0 preferred, 1 other, 2 under the minimum rating.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="minimumRating">Items with a known rating under this go last; 0 means off.</param>
    /// <param name="languages">Preferred original languages.</param>
    /// <param name="countries">Preferred production countries.</param>
    /// <param name="tags">Preferred tags or genres.</param>
    /// <param name="prefixes">Preferred title prefixes.</param>
    /// <returns>The group number.</returns>
    internal static int GroupOf(BaseItem item, double minimumRating, string[] languages, string[] countries, string[] tags, string[] prefixes)
    {
        if (minimumRating > 0 && item.CommunityRating is float rating && rating < minimumRating)
        {
            return 2;
        }

        return IsPreferred(item, languages, countries, tags, prefixes) ? 0 : 1;
    }

    /// <summary>
    /// Returns whether the item matches a preferred language, country, tag, genre or title prefix.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="languages">Preferred original languages.</param>
    /// <param name="countries">Preferred production countries.</param>
    /// <param name="tags">Preferred tags or genres.</param>
    /// <param name="prefixes">Preferred title prefixes.</param>
    /// <returns><c>true</c> when the item matches.</returns>
    internal static bool IsPreferred(BaseItem item, string[] languages, string[] countries, string[] tags, string[] prefixes)
    {
        if (!string.IsNullOrEmpty(item.OriginalLanguage)
            && languages.Any(l => string.Equals(l, item.OriginalLanguage, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (item.ProductionLocations.Any(loc => countries.Any(c => loc.Contains(c, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        if (item.Tags.Concat(item.Genres).Any(t => tags.Any(p => string.Equals(p, t, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        var titles = new[] { item.Name, item.OriginalTitle };
        return titles.Any(t => !string.IsNullOrEmpty(t) && prefixes.Any(p => t.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Returns the item's score inside its group.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="ratingWeight">Weight of the rating (0-10).</param>
    /// <param name="recencyWeight">Weight of recency.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <returns>The score; higher is earlier.</returns>
    internal static double Score(BaseItem item, double ratingWeight, double recencyWeight, DateTime nowUtc)
    {
        var rating = item.CommunityRating ?? 5f;
        var year = item.PremiereDate?.Year ?? item.ProductionYear;
        var recency = year is int y ? Math.Clamp(1 - ((nowUtc.Year - y) / 10.0), 0, 1) : 0;
        var addedBonus = item.DateCreated > nowUtc.AddDays(-30) ? 0.5 : 0;
        return (ratingWeight * rating) + (recencyWeight * (recency + addedBonus));
    }

    private static string[] Split(string? list)
    {
        return string.IsNullOrWhiteSpace(list)
            ? Array.Empty<string>()
            : list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
