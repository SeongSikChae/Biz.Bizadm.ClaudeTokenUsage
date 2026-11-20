using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Biz.Bizadm.ClaudeTokenUsage.Core;
using Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Storage;
using Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Services;

namespace Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Tests;

[TestClass]
public sealed class UsageMaintenanceServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 14, 37, 12);

    private static async Task<long> HourTotalAsync(DuckDbUsageStore store)
        => (await store.QuerySeriesAsync(new UsageQuery(UsageUnit.Hour, Now.Date, Now.Date.AddDays(1), UsageGroupBy.TokenType, [], [], []))).TotalTokens;

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (int i = 0; i < 100; i++)
        {
            if (await condition())
                return;
            await Task.Delay(20);
        }
        Assert.Fail("Condition was not met in time.");
    }

    [TestMethod]
    public async Task StartAsync_RunsMaintenanceImmediately_ThenEveryMinute()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Tests." + Guid.NewGuid().ToString("N"));
        FakeTimeProvider time = new(KstClock.FromKst(Now));
        try
        {
            using DuckDbUsageStore store = DuckDbUsageStore.Open(Path.Combine(directory, "usage.duckdb"), time);
            await store.IngestAsync([new TokenUsagePoint(KstClock.FromKst(Now.AddMinutes(-2)), "u", "m", "input", 10)], []);
            Assert.AreEqual(0, await HourTotalAsync(store)); // 유지 작업 전에는 시 단위에 없음

            using UsageMaintenanceService service = new(store, time, NullLogger<UsageMaintenanceService>.Instance);
            await service.StartAsync(CancellationToken.None);
            Assert.AreEqual(10, await HourTotalAsync(store)); // 시작 직후 한 번 실행

            await store.IngestAsync([new TokenUsagePoint(KstClock.FromKst(Now), "u", "m", "input", 5)], []);
            Assert.AreEqual(10, await HourTotalAsync(store));

            time.Advance(TimeSpan.FromSeconds(59));
            await Task.Delay(100);
            Assert.AreEqual(10, await HourTotalAsync(store)); // 1분이 지나기 전에는 실행하지 않음

            time.Advance(TimeSpan.FromSeconds(1));
            await WaitUntilAsync(async () => await HourTotalAsync(store) == 15); // 1분 주기 실행

            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
