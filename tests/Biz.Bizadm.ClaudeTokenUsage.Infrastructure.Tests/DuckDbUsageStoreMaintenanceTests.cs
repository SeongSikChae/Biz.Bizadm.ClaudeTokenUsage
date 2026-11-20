using Biz.Bizadm.ClaudeTokenUsage.Core;
using static Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Tests.StoreFixture;

namespace Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Tests;

[TestClass]
public sealed class DuckDbUsageStoreMaintenanceTests
{
    private static readonly DateTime Start = new(2026, 11, 20, 9, 0, 0);

    /// <summary>
    /// 09:00부터 1시간 동안 10분마다 사용자 둘·모델 둘·토큰 종류 둘로 값을 넣는다.
    /// </summary>
    private static async Task SeedAsync(StoreFixture fixture, DateTime from, int hours)
    {
        for (DateTime t = from; t < from.AddHours(hours); t = t.AddMinutes(10))
        {
            int n = t.Minute + t.Hour;
            await fixture.Store.IngestAsync(
                [
                    Token(t, 100 + n, "input", "a@example.com", "claude-opus-5-5"),
                    Token(t, 10 + n, "output", "b@example.com", "claude-sonnet-5-5"),
                ],
                [Cost(t, 0.01 * n, "a@example.com", "claude-opus-5-5")]);
        }
    }

    [TestMethod]
    public async Task Recompute_ParentTotalsEqualChildTotals()
    {
        using StoreFixture fixture = new(Start);
        await SeedAsync(fixture, Start, 3);
        fixture.SetNow(Start.AddHours(3).AddMinutes(1));
        await fixture.Store.RunMaintenanceAsync();

        foreach (UsageUnit unit in UsageUnitExtensions.RolledUp)
        {
            UsageUnit child = unit.Child()!.Value;
            var (parentTokens, parentCosts) = await fixture.Store.ReadRowsAsync(unit);
            var (childTokens, childCosts) = await fixture.Store.ReadRowsAsync(child);

            var expected = childTokens
                .GroupBy(row => (BucketCalculator.Floor(unit, row.Bucket), row.User, row.Model, row.Type))
                .ToDictionary(group => group.Key, group => group.Sum(row => row.Tokens));
            var actual = parentTokens.ToDictionary(row => (row.Bucket, row.User, row.Model, row.Type), row => row.Tokens);
            CollectionAssert.AreEquivalent(expected.ToList(), actual.ToList(), unit.ToString());

            var expectedCost = childCosts
                .GroupBy(row => (BucketCalculator.Floor(unit, row.Bucket), row.User, row.Model))
                .ToDictionary(group => group.Key, group => group.Sum(row => row.Cost));
            Assert.HasCount(expectedCost.Count, parentCosts, unit.ToString());
            foreach (var row in parentCosts)
                Assert.AreEqual(expectedCost[(row.Bucket, row.User, row.Model)], row.Cost, 1e-9, unit.ToString());
        }
    }

    [TestMethod]
    public async Task Retention_DeletesOldRows_AndKeepsParentTotals()
    {
        using StoreFixture fixture = new(Start);
        await SeedAsync(fixture, Start, 1);
        fixture.SetNow(Start.AddMinutes(61));
        await fixture.Store.RunMaintenanceAsync();
        long total = await fixture.SumAsync(UsageUnit.Year);
        double totalCost = await fixture.CostSumAsync(UsageUnit.Year);
        Assert.IsGreaterThan(0, total);

        fixture.SetNow(Start.AddDays(2));
        await fixture.Store.RunMaintenanceAsync();
        Assert.AreEqual(0, await fixture.RowCountAsync(UsageUnit.Minute));
        Assert.AreEqual(total, await fixture.SumAsync(UsageUnit.Hour));

        fixture.SetNow(Start.AddDays(9));
        await fixture.Store.RunMaintenanceAsync();
        Assert.AreEqual(0, await fixture.RowCountAsync(UsageUnit.Hour));
        Assert.AreEqual(total, await fixture.SumAsync(UsageUnit.Day));

        fixture.SetNow(Start.AddDays(40));
        await fixture.Store.RunMaintenanceAsync();
        Assert.AreEqual(0, await fixture.RowCountAsync(UsageUnit.Day));
        Assert.AreEqual(total, await fixture.SumAsync(UsageUnit.Week));
        Assert.AreEqual(total, await fixture.SumAsync(UsageUnit.Month));
        Assert.AreEqual(total, await fixture.SumAsync(UsageUnit.Year));
        Assert.AreEqual(totalCost, await fixture.CostSumAsync(UsageUnit.Year), 1e-9);
    }

    [TestMethod]
    public async Task Retention_KeepsRowsInsideRetention()
    {
        using StoreFixture fixture = new(Start);
        await SeedAsync(fixture, Start, 1);
        fixture.SetNow(Start.AddHours(23));
        await fixture.Store.RunMaintenanceAsync();

        Assert.AreEqual(6, (await fixture.Store.ReadRowsAsync(UsageUnit.Minute)).Tokens.Count / 2);
    }

    [TestMethod]
    public async Task Reopen_KeepsData()
    {
        using StoreFixture fixture = new(Start);
        await SeedAsync(fixture, Start, 1);
        await fixture.Store.RunMaintenanceAsync();
        long minute = await fixture.SumAsync(UsageUnit.Minute);
        long year = await fixture.SumAsync(UsageUnit.Year);

        fixture.Reopen();

        Assert.AreEqual(minute, await fixture.SumAsync(UsageUnit.Minute));
        Assert.AreEqual(year, await fixture.SumAsync(UsageUnit.Year));
    }

    [TestMethod]
    public async Task FirstMaintenanceAfterThreeDayGap_CatchesUp()
    {
        using StoreFixture fixture = new(Start);
        await SeedAsync(fixture, Start, 2);
        long total = await fixture.SumAsync(UsageUnit.Minute);
        fixture.Reopen(); // 서버 재시작

        fixture.SetNow(Start.AddDays(3));
        await fixture.Store.RunMaintenanceAsync(); // 밀린 재계산과 삭제를 한 번에

        Assert.AreEqual(0, await fixture.RowCountAsync(UsageUnit.Minute));
        Assert.AreEqual(total, await fixture.SumAsync(UsageUnit.Hour));
        Assert.AreEqual(total, await fixture.SumAsync(UsageUnit.Day, Start.Date));
        foreach (UsageUnit unit in new[] { UsageUnit.Week, UsageUnit.Month, UsageUnit.Year })
            Assert.AreEqual(total, await fixture.SumAsync(unit), unit.ToString());
    }
}
