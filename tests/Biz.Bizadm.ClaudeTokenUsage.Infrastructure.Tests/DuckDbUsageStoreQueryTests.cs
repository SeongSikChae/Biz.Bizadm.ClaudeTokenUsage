using Biz.Bizadm.ClaudeTokenUsage.Core;
using Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Storage;
using static Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Tests.StoreFixture;

namespace Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Tests;

[TestClass]
public sealed class DuckDbUsageStoreQueryTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 14, 37, 12);

    private static UsageQuery Query(UsageUnit unit, DateTime from, DateTime to, UsageGroupBy groupBy = UsageGroupBy.TokenType,
        string[]? users = null, string[]? models = null, string[]? tokenTypes = null)
        => new(unit, from, to, groupBy, users ?? [], models ?? [], tokenTypes ?? []);

    private static async Task<StoreFixture> SeededAsync()
    {
        StoreFixture fixture = new(Now);
        await fixture.Store.IngestAsync(
            [
                Token(new DateTime(2026, 11, 20, 10, 5, 0), 100, "input"),
                Token(new DateTime(2026, 11, 20, 10, 6, 0), 30, "output"),
                Token(new DateTime(2026, 11, 20, 12, 0, 0), 7, "input", "b@example.com", "claude-sonnet-5-5"),
            ],
            [
                Cost(new DateTime(2026, 11, 20, 10, 5, 0), 0.25),
                Cost(new DateTime(2026, 11, 20, 12, 0, 0), 0.5, "b@example.com", "claude-sonnet-5-5"),
            ]);
        await fixture.Store.RunMaintenanceAsync();
        return fixture;
    }

    [TestMethod]
    public async Task Series_FillsEmptyBucketsWithZero()
    {
        using StoreFixture fixture = await SeededAsync();

        UsageSeries series = await fixture.Store.QuerySeriesAsync(Query(UsageUnit.Hour, new DateTime(2026, 11, 20, 9, 0, 0), new DateTime(2026, 11, 20, 13, 0, 0)));

        CollectionAssert.AreEqual(
            new[] { 9, 10, 11, 12 }.Select(h => new DateTime(2026, 11, 20, h, 0, 0)).ToArray(),
            series.Buckets.Select(b => b.StartKst).ToArray());
        CollectionAssert.AreEqual(new long[] { 0, 130, 0, 7 }, series.Buckets.Select(b => b.TotalTokens).ToArray());
        Assert.AreEqual(100, series.Buckets[1].Tokens["input"]);
        Assert.AreEqual(30, series.Buckets[1].Tokens["output"]);
        CollectionAssert.AreEqual(new[] { "input", "output" }, series.Series.ToArray());
        Assert.AreEqual(137, series.TotalTokens);
        Assert.AreEqual(0.75, series.TotalCostUsd!.Value, 1e-9);
        Assert.AreEqual(0d, series.Buckets[0].CostUsd);
    }

    [TestMethod]
    public async Task Series_RoundsFromDown_AndKeepsAlignedTo()
    {
        using StoreFixture fixture = await SeededAsync();

        UsageSeries aligned = await fixture.Store.QuerySeriesAsync(Query(UsageUnit.Hour, new DateTime(2026, 11, 20, 10, 30, 0), new DateTime(2026, 11, 20, 12, 0, 0)));
        Assert.AreEqual(new DateTime(2026, 11, 20, 10, 0, 0), aligned.FromKst);
        Assert.AreEqual(new DateTime(2026, 11, 20, 12, 0, 0), aligned.ToKst);
        Assert.HasCount(2, aligned.Buckets); // 10시, 11시 (12시는 넣지 않음)

        UsageSeries rounded = await fixture.Store.QuerySeriesAsync(Query(UsageUnit.Hour, new DateTime(2026, 11, 20, 10, 30, 0), new DateTime(2026, 11, 20, 12, 0, 1)));
        Assert.AreEqual(new DateTime(2026, 11, 20, 13, 0, 0), rounded.ToKst);
        Assert.HasCount(3, rounded.Buckets);
    }

    [TestMethod]
    public async Task Series_ExcludesFutureBuckets()
    {
        using StoreFixture fixture = await SeededAsync();

        UsageSeries series = await fixture.Store.QuerySeriesAsync(Query(UsageUnit.Hour, new DateTime(2026, 11, 20, 12, 0, 0), new DateTime(2026, 11, 21)));

        Assert.AreEqual(new DateTime(2026, 11, 20, 14, 0, 0), series.Buckets[^1].StartKst); // 현재 구간까지
        Assert.HasCount(3, series.Buckets);
    }

    [TestMethod]
    public async Task Series_ClipsToRetention_AndReturnsEmptyWhenAllOutside()
    {
        using StoreFixture fixture = await SeededAsync();

        UsageSeries clipped = await fixture.Store.QuerySeriesAsync(Query(UsageUnit.Minute, new DateTime(2026, 11, 17), new DateTime(2026, 11, 19, 14, 40, 0)));
        Assert.AreEqual(new DateTime(2026, 11, 19, 14, 37, 0), clipped.Buckets[0].StartKst);
        Assert.HasCount(3, clipped.Buckets);

        UsageSeries outside = await fixture.Store.QuerySeriesAsync(Query(UsageUnit.Minute, new DateTime(2026, 11, 17), new DateTime(2026, 11, 18)));
        Assert.IsEmpty(outside.Buckets);
        Assert.AreEqual(0, outside.TotalTokens);
    }

    [TestMethod]
    public async Task Series_WeekLowerBound_IsMonday()
    {
        using StoreFixture fixture = await SeededAsync();

        UsageSeries series = await fixture.Store.QuerySeriesAsync(Query(UsageUnit.Week, new DateTime(2024, 1, 1), new DateTime(2026, 12, 1)));

        Assert.AreEqual(new DateTime(2025, 11, 17), series.Buckets[0].StartKst);
        Assert.IsTrue(series.Buckets.All(b => b.StartKst.DayOfWeek == DayOfWeek.Monday));
        Assert.AreEqual(new DateTime(2026, 11, 16), series.Buckets[^1].StartKst);
        Assert.AreEqual(137, series.TotalTokens);
    }

    [TestMethod]
    public async Task Series_TokenTypeFilter_HidesCost()
    {
        using StoreFixture fixture = await SeededAsync();

        UsageSeries series = await fixture.Store.QuerySeriesAsync(Query(UsageUnit.Day, new DateTime(2026, 11, 20), new DateTime(2026, 11, 21), tokenTypes: ["input"]));

        Assert.AreEqual(107, series.TotalTokens);
        Assert.IsNull(series.TotalCostUsd);
        Assert.IsTrue(series.Buckets.All(b => b.CostUsd is null));
    }

    [TestMethod]
    public async Task Series_GroupByModelWithUserFilter_FiltersTokensAndCost()
    {
        using StoreFixture fixture = await SeededAsync();

        UsageSeries series = await fixture.Store.QuerySeriesAsync(Query(UsageUnit.Day, new DateTime(2026, 11, 20), new DateTime(2026, 11, 21),
            UsageGroupBy.Model, users: ["b@example.com"]));

        CollectionAssert.AreEqual(new[] { "claude-sonnet-5-5" }, series.Series.ToArray());
        Assert.AreEqual(7, series.TotalTokens);
        Assert.AreEqual(0.5, series.TotalCostUsd!.Value, 1e-9);
    }

    [TestMethod]
    public async Task Meta_ReturnsRetentionAndDistinctValues()
    {
        using StoreFixture fixture = await SeededAsync();

        UsageMeta meta = await fixture.Store.QueryMetaAsync();

        Assert.AreEqual(Now, meta.NowKst);
        Assert.AreEqual(new DateTime(2026, 11, 19, 14, 37, 0), meta.Units.Single(u => u.Unit == UsageUnit.Minute).EarliestKst);
        Assert.AreEqual(new DateTime(2025, 11, 17), meta.Units.Single(u => u.Unit == UsageUnit.Week).EarliestKst);
        Assert.IsNull(meta.Units.Single(u => u.Unit == UsageUnit.Year).EarliestKst);
        CollectionAssert.AreEqual(new[] { "a@example.com", "b@example.com" }, meta.Users.ToArray());
        CollectionAssert.AreEqual(new[] { "claude-opus-5-5", "claude-sonnet-5-5" }, meta.Models.ToArray());
        CollectionAssert.AreEqual(new[] { "input", "output" }, meta.TokenTypes.ToArray());
    }
}
