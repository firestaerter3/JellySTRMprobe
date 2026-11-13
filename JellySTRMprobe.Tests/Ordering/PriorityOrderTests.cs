using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using JellySTRMprobe.Ordering;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace JellySTRMprobe.Tests.Ordering;

public class PriorityOrderTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);

    private static BaseItem Item(string name, float? rating = null, int? year = null, string? language = null, string[]? countries = null, DateTime? added = null)
    {
        var item = TestHelpers.CreateTestItem(name, "/m/" + name + ".strm");
        item.CommunityRating = rating;
        item.ProductionYear = year;
        item.OriginalLanguage = language;
        item.ProductionLocations = countries ?? Array.Empty<string>();
        item.DateCreated = added ?? Now.AddYears(-1);
        return item;
    }

    private static PluginConfiguration Config(Action<PluginConfiguration>? change = null)
    {
        var config = new PluginConfiguration { PriorityLanguages = string.Empty, OrderRandomnessBand = 0 };
        change?.Invoke(config);
        return config;
    }

    [Fact]
    public void PreferredLanguageComesFirst()
    {
        var foreign = Item("Foreign", rating: 9, year: 2026);
        var preferred = Item("Preferred", rating: 4, year: 1990, language: "de");

        var order = PriorityOrder.Sort(new[] { foreign, preferred }, Config(c => c.PriorityLanguages = "de"), Now, new Random(1));

        order.Should().Equal(preferred, foreign);
    }

    [Fact]
    public void PreferredCountryMatchesPartOfALocation()
    {
        var item = Item("X", countries: new[] { "Federal Republic of Germany" });

        PriorityOrder.IsPreferred(item, Array.Empty<string>(), new[] { "Germany" }, Array.Empty<string>(), Array.Empty<string>()).Should().BeTrue();
    }

    [Fact]
    public void TitlePrefixAndTagMatch()
    {
        var prefixed = Item("[DE] Something");
        var tagged = Item("Tagged");
        tagged.Tags = new[] { "German" };

        PriorityOrder.IsPreferred(prefixed, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), new[] { "[DE]" }).Should().BeTrue();
        PriorityOrder.IsPreferred(tagged, Array.Empty<string>(), Array.Empty<string>(), new[] { "german" }, Array.Empty<string>()).Should().BeTrue();
    }

    [Fact]
    public void EmptyPreferencesPreferNothing()
    {
        var item = Item("X", language: "en", countries: new[] { "United States" });

        PriorityOrder.IsPreferred(item, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()).Should().BeFalse();
    }

    [Fact]
    public void HigherRatingAndNewerGoFirstWithStrictBand()
    {
        var oldLow = Item("OldLow", rating: 5, year: 1980);
        var newHigh = Item("NewHigh", rating: 8, year: 2026);
        var oldHigh = Item("OldHigh", rating: 8, year: 1980);

        var order = PriorityOrder.Sort(new[] { oldLow, oldHigh, newHigh }, Config(), Now, new Random(1));

        order.Should().Equal(newHigh, oldHigh, oldLow);
    }

    [Fact]
    public void MinimumRatingPutsLowRatedLast()
    {
        var low = Item("Low", rating: 3, year: 2026);
        var unknown = Item("Unknown", rating: null, year: 1970);

        var order = PriorityOrder.Sort(new[] { low, unknown }, Config(c => c.MinimumRating = 5), Now, new Random(1));

        order.Should().Equal(unknown, low);
    }

    [Fact]
    public void ZeroWeightsIgnoreRatingAndRecency()
    {
        var a = Item("A", rating: 9, year: 2026);
        var b = Item("B", rating: 1, year: 1950);

        PriorityOrder.Score(a, 0, 0, Now).Should().Be(0);
        PriorityOrder.Score(b, 0, 0, Now).Should().Be(0);
    }

    [Fact]
    public void RecentlyAddedGetsABonus()
    {
        var added = Item("Added", rating: 5, year: 1950, added: Now.AddDays(-2));
        var old = Item("Old", rating: 5, year: 1950);

        PriorityOrder.Score(added, 1, 3, Now).Should().BeGreaterThan(PriorityOrder.Score(old, 1, 3, Now));
    }

    [Fact]
    public void InsideABandTheOrderVaries()
    {
        var items = Enumerable.Range(0, 20).Select(i => Item("Same" + i, rating: 7, year: 2020)).ToList<BaseItem>();
        var config = Config(c => c.OrderRandomnessBand = 1);

        var first = PriorityOrder.Sort(items, config, Now, new Random(1)).Select(i => i.Name).ToList();
        var second = PriorityOrder.Sort(items, config, Now, new Random(2)).Select(i => i.Name).ToList();

        first.Should().BeEquivalentTo(second);
        first.Should().NotEqual(second);
    }

    [Fact]
    public void Defaults_PreferEnglishAndHaveNoCountryPreference()
    {
        var config = new PluginConfiguration();

        config.PrioritizeOrder.Should().BeTrue();
        config.PriorityLanguages.Should().Be("en");
        config.PriorityCountries.Should().BeEmpty();
        config.PriorityTags.Should().BeEmpty();
        config.PriorityTitlePrefixes.Should().BeEmpty();
        config.EnableStreamCache.Should().BeTrue();
        config.VerifyAfterRestore.Should().BeTrue();
    }
}
