using OpenTelemetry.Proto.Metrics.V1;
using Biz.Bizadm.ClaudeTokenUsage.Core;
using Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Otlp;
using static Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Tests.StoreFixture;

namespace Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Tests;

[TestClass]
public sealed class DuckDbUsageStoreIngestTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 14, 5, 0);

    [TestMethod]
    public async Task SameMinuteAndKey_IsSummed()
    {
        using StoreFixture fixture = new(Now);
        await fixture.IngestAsync(Token(new DateTime(2026, 11, 20, 14, 1, 10), 100));
        await fixture.IngestAsync(Token(new DateTime(2026, 11, 20, 14, 1, 50), 23));

        var (tokens, _) = await fixture.Store.ReadRowsAsync(UsageUnit.Minute);
        Assert.HasCount(1, tokens);
        Assert.AreEqual((new DateTime(2026, 11, 20, 14, 1, 0), "a@example.com", "claude-opus-5-5", "input", 123L), tokens[0]);
    }

    [TestMethod]
    public async Task EndTime_DeterminesMinuteBucket_InKst()
    {
        using StoreFixture fixture = new(Now);
        // UTC 05:02:59.900 = KST 14:02:59.900 → 14:02 구간
        TokenUsagePoint point = new(new DateTimeOffset(2026, 11, 20, 5, 2, 59, 900, TimeSpan.Zero), "u", "m", "output", 7);
        await fixture.Store.IngestAsync([point], []);

        Assert.AreEqual(7, await fixture.SumAsync(UsageUnit.Minute, new DateTime(2026, 11, 20, 14, 2, 0)));
    }

    [TestMethod]
    public async Task ResolvedResourceUser_IsStoredAsRowUser()
    {
        using StoreFixture fixture = new(Now);
        var request = OtlpRequests.Request(
            [OtlpRequests.Attr("user.email", "resource@example.com")],
            OtlpRequests.Sum(UsageAttributes.TokenUsageMetric, AggregationTemporality.Delta,
                OtlpRequests.IntPoint(KstClock.FromKst(new DateTime(2026, 11, 20, 14, 3, 0)), 9, OtlpRequests.Attr("type", "cacheRead"))));
        OtlpMetricsBatch batch = OtlpMetricsParser.Extract(request);

        await fixture.Store.IngestAsync(batch.Tokens, batch.Costs);

        var (tokens, _) = await fixture.Store.ReadRowsAsync(UsageUnit.Minute);
        Assert.AreEqual("resource@example.com", tokens.Single().User);
        Assert.AreEqual("cacheRead", tokens.Single().Type);
    }

    [TestMethod]
    public async Task LateValueForFinalizedHour_IsCountedOncePerUnit()
    {
        using StoreFixture fixture = new(new DateTime(2026, 11, 20, 13, 30, 30));
        await fixture.IngestAsync(Token(new DateTime(2026, 11, 20, 13, 30, 0), 10));
        await fixture.Store.RunMaintenanceAsync();

        fixture.SetNow(Now); // 14:05 → 13시 구간 확정
        await fixture.Store.RunMaintenanceAsync();
        await fixture.IngestAsync(Token(new DateTime(2026, 11, 20, 13, 40, 0), 5)); // 늦게 온 값
        await fixture.Store.RunMaintenanceAsync();

        Assert.AreEqual(15, await fixture.SumAsync(UsageUnit.Minute));
        Assert.AreEqual(15, await fixture.SumAsync(UsageUnit.Hour, new DateTime(2026, 11, 20, 13, 0, 0)));
        foreach (UsageUnit unit in new[] { UsageUnit.Day, UsageUnit.Week, UsageUnit.Month, UsageUnit.Year })
            Assert.AreEqual(15, await fixture.SumAsync(unit), unit.ToString());
    }

    [TestMethod]
    public async Task LateValuePastMinuteRetention_GoesToHourAndAbove()
    {
        using StoreFixture fixture = new(Now);
        await fixture.Store.RunMaintenanceAsync();

        DateTime late = Now.AddHours(-30); // 11-19 08:05: 분 보관(24시간) 밖, 시·일은 확정 구간
        await fixture.Store.IngestAsync([Token(late, 8)], [Cost(late, 0.5)]);
        await fixture.Store.RunMaintenanceAsync();

        Assert.AreEqual(0, await fixture.SumAsync(UsageUnit.Minute));
        Assert.AreEqual(8, await fixture.SumAsync(UsageUnit.Hour, new DateTime(2026, 11, 19, 8, 0, 0)));
        Assert.AreEqual(8, await fixture.SumAsync(UsageUnit.Day, new DateTime(2026, 11, 19)));
        foreach (UsageUnit unit in new[] { UsageUnit.Week, UsageUnit.Month, UsageUnit.Year })
        {
            Assert.AreEqual(8, await fixture.SumAsync(unit), unit.ToString());
            Assert.AreEqual(0.5, await fixture.CostSumAsync(unit), 1e-9, unit.ToString());
        }
    }

    [TestMethod]
    public async Task LateValueAfterDayDeleted_GoesToWeekMonthYearOnce()
    {
        using StoreFixture fixture = new(Now);
        await fixture.Store.RunMaintenanceAsync();

        DateTime late = new(2026, 9, 15, 10, 0, 0); // 일 보관(10-20 이후) 밖, 주(1년)·월(5년)은 확정 구간
        await fixture.IngestAsync(Token(late, 21));
        await fixture.Store.RunMaintenanceAsync();

        Assert.AreEqual(0, await fixture.SumAsync(UsageUnit.Hour));
        Assert.AreEqual(0, await fixture.SumAsync(UsageUnit.Day));
        Assert.AreEqual(21, await fixture.SumAsync(UsageUnit.Week, new DateTime(2026, 9, 14)));
        Assert.AreEqual(21, await fixture.SumAsync(UsageUnit.Month, new DateTime(2026, 9, 1)));
        Assert.AreEqual(21, await fixture.SumAsync(UsageUnit.Year, new DateTime(2026, 1, 1)));
    }

    [TestMethod]
    public async Task ValueBeforeMaintenance_IsOnlyInMinuteUntilRecompute()
    {
        using StoreFixture fixture = new(Now);
        await fixture.IngestAsync(Token(Now.AddMinutes(-1), 4));

        Assert.AreEqual(4, await fixture.SumAsync(UsageUnit.Minute));
        Assert.AreEqual(0, await fixture.SumAsync(UsageUnit.Hour));

        await fixture.Store.RunMaintenanceAsync();
        foreach (UsageUnit unit in UsageUnitExtensions.All)
            Assert.AreEqual(4, await fixture.SumAsync(unit), unit.ToString());
    }
}
