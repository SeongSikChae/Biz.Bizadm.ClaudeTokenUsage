using Microsoft.Extensions.Time.Testing;
using Biz.Bizadm.ClaudeTokenUsage.Core;
using Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Storage;

namespace Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Tests;

/// <summary>
/// 임시 DB 파일과 가짜 시계로 저장소를 여는 테스트 도우미.
/// </summary>
internal sealed class StoreFixture : IDisposable
{
    private readonly string directory;

    public StoreFixture(DateTime nowKst)
    {
        directory = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Tests." + Guid.NewGuid().ToString("N"));
        DatabasePath = Path.Combine(directory, "data", "usage.duckdb");
        Time = new FakeTimeProvider(KstClock.FromKst(nowKst));
        Store = DuckDbUsageStore.Open(DatabasePath, Time);
    }

    public string DatabasePath { get; }

    public FakeTimeProvider Time { get; }

    public DuckDbUsageStore Store { get; private set; }

    public void SetNow(DateTime nowKst) => Time.SetUtcNow(KstClock.FromKst(nowKst));

    public void Reopen()
    {
        Store.Dispose();
        Store = DuckDbUsageStore.Open(DatabasePath, Time);
    }

    public static TokenUsagePoint Token(DateTime kst, long tokens, string type = "input", string user = "a@example.com", string model = "claude-opus-5-5")
        => new(KstClock.FromKst(kst), user, model, type, tokens);

    public static CostUsagePoint Cost(DateTime kst, double cost, string user = "a@example.com", string model = "claude-opus-5-5")
        => new(KstClock.FromKst(kst), user, model, cost);

    public Task IngestAsync(params TokenUsagePoint[] tokens) => Store.IngestAsync(tokens, []);

    public async Task<long> SumAsync(UsageUnit unit, DateTime? bucket = null)
    {
        var (tokens, _) = await Store.ReadRowsAsync(unit);
        return tokens.Where(row => bucket is null || row.Bucket == bucket).Sum(row => row.Tokens);
    }

    public async Task<double> CostSumAsync(UsageUnit unit, DateTime? bucket = null)
    {
        var (_, costs) = await Store.ReadRowsAsync(unit);
        return costs.Where(row => bucket is null || row.Bucket == bucket).Sum(row => row.Cost);
    }

    public async Task<int> RowCountAsync(UsageUnit unit) => (await Store.ReadRowsAsync(unit)).Tokens.Count;

    public void Dispose()
    {
        Store.Dispose();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
