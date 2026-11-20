using Biz.Bizadm.ClaudeTokenUsage.Core;

namespace Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Storage
{
    /// <summary>
    /// 나눠 볼 기준.
    /// </summary>
    public enum UsageGroupBy
    {
        TokenType,
        Model,
        User,
    }

    public static class UsageGroupByExtensions
    {
        public static string ToName(this UsageGroupBy groupBy) => groupBy switch
        {
            UsageGroupBy.TokenType => "tokenType",
            UsageGroupBy.Model => "model",
            UsageGroupBy.User => "user",
            _ => throw new ArgumentOutOfRangeException(nameof(groupBy), groupBy, null),
        };

        public static bool TryParse(string? name, out UsageGroupBy groupBy)
        {
            foreach (UsageGroupBy candidate in Enum.GetValues<UsageGroupBy>())
            {
                if (string.Equals(candidate.ToName(), name, StringComparison.Ordinal))
                {
                    groupBy = candidate;
                    return true;
                }
            }
            groupBy = default;
            return false;
        }

        internal static string ToColumn(this UsageGroupBy groupBy) => groupBy switch
        {
            UsageGroupBy.TokenType => "token_type",
            UsageGroupBy.Model => "model",
            UsageGroupBy.User => "user_id",
            _ => throw new ArgumentOutOfRangeException(nameof(groupBy), groupBy, null),
        };
    }

    /// <summary>
    /// 조회 입력. 시각은 KST 벽시계이며 [<see cref="FromKst"/>, <see cref="ToKst"/>) 반열림이다.
    /// 빈 필터 목록은 전체를 뜻한다.
    /// </summary>
    public sealed record UsageQuery(
        UsageUnit Unit,
        DateTime FromKst,
        DateTime ToKst,
        UsageGroupBy GroupBy,
        IReadOnlyList<string> Users,
        IReadOnlyList<string> Models,
        IReadOnlyList<string> TokenTypes);
}
