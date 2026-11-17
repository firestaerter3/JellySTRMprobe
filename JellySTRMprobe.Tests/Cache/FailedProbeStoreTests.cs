using System;
using System.IO;
using FluentAssertions;
using JellySTRMprobe.Cache;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellySTRMprobe.Tests.Cache;

public sealed class FailedProbeStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "strmprobe-failedstore-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 17, 3, 0, 0, DateTimeKind.Utc);

    private FailedProbeStore Store() => new(() => Path.Combine(_dir, "failed-probes.json"), () => _now, NullLogger.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void NothingRecorded_NothingSkipped()
    {
        var item = TestHelpers.CreateTestItem("A", "/m/a.strm");

        Store().RecentlyFailed(new[] { item }, 7).Should().BeEmpty();
    }

    [Fact]
    public void FailedItem_IsSkippedForTheConfiguredDays_AcrossInstances()
    {
        var item = TestHelpers.CreateTestItem("A", "/m/a.strm");
        Store().Record(new[] { item }, 7);

        _now = _now.AddDays(6);
        Store().RecentlyFailed(new[] { item }, 7).Should().Contain(item.Id);

        _now = _now.AddDays(2);
        Store().RecentlyFailed(new[] { item }, 7).Should().BeEmpty();
    }

    [Fact]
    public void SkipDaysZero_SkipsNothing()
    {
        var item = TestHelpers.CreateTestItem("A", "/m/a.strm");
        Store().Record(new[] { item }, 7);

        Store().RecentlyFailed(new[] { item }, 0).Should().BeEmpty();
    }

    [Fact]
    public void ItemWithoutPath_IsNeverRecorded()
    {
        var item = TestHelpers.CreateTestItem("A", null);
        Store().Record(new[] { item }, 7);

        Store().RecentlyFailed(new[] { item }, 7).Should().BeEmpty();
    }

    [Fact]
    public void OldEntries_AreDroppedOnRecord()
    {
        var old = TestHelpers.CreateTestItem("Old", "/m/old.strm");
        var fresh = TestHelpers.CreateTestItem("Fresh", "/m/fresh.strm");
        Store().Record(new[] { old }, 7);

        _now = _now.AddDays(30);
        Store().Record(new[] { fresh }, 7);

        File.ReadAllText(Path.Combine(_dir, "failed-probes.json")).Should().NotContain("/m/old.strm");
    }

    [Fact]
    public void DamagedFile_MeansNothingIsSkipped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "failed-probes.json"), "{ not json");
        var item = TestHelpers.CreateTestItem("A", "/m/a.strm");

        Store().RecentlyFailed(new[] { item }, 7).Should().BeEmpty();
    }
}
