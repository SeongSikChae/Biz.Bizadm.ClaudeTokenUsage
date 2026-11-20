using Biz.Bizadm.ClaudeTokenUsage.Core;

namespace Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Storage
{
    /// <summary>
    /// 한 구간의 합계. <see cref="Tokens"/>는 값이 있는 계열만 담는다. <see cref="CostUsd"/>는 토큰 종류 필터가 있으면 null이다.
    /// </summary>
    public sealed record UsageBucket(DateTime StartKst, IReadOnlyDictionary<string, long> Tokens, long TotalTokens, double? CostUsd);

    /// <summary>
    /// 조회 결과. <see cref="FromKst"/>·<see cref="ToKst"/>는 구간 경계로 맞춘 조회 기간이고,
    /// <see cref="Buckets"/>는 그중 보관 기간 안이면서 현재 구간까지인 구간을 빠짐없이 담는다(사용량이 없으면 0).
    /// </summary>
    public sealed record UsageSeries(
        UsageUnit Unit,
        DateTime FromKst,
        DateTime ToKst,
        UsageGroupBy GroupBy,
        IReadOnlyList<string> Series,
        IReadOnlyList<UsageBucket> Buckets,
        long TotalTokens,
        double? TotalCostUsd);

    /// <summary>
    /// 단위별 보관 하한. 년은 null(영구).
    /// </summary>
    public sealed record UsageUnitRetention(UsageUnit Unit, DateTime? EarliestKst);

    /// <summary>
    /// 화면 필터용 정보.
    /// </summary>
    public sealed record UsageMeta(
        DateTime NowKst,
        IReadOnlyList<UsageUnitRetention> Units,
        IReadOnlyList<string> Users,
        IReadOnlyList<string> Models,
        IReadOnlyList<string> TokenTypes);
}
