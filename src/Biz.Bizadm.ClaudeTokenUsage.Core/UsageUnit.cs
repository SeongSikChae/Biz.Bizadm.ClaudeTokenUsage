namespace Biz.Bizadm.ClaudeTokenUsage.Core
{
    /// <summary>
    /// 롤업 단위. 분이 가장 세밀하고, 시는 분을, 일은 시를, 주·월은 일을, 년은 월을 모아 만든다.
    /// </summary>
    public enum UsageUnit
    {
        Minute,
        Hour,
        Day,
        Week,
        Month,
        Year,
    }

    public static class UsageUnitExtensions
    {
        /// <summary>
        /// 모든 단위(분 → 년 순).
        /// </summary>
        public static IReadOnlyList<UsageUnit> All { get; } =
            [UsageUnit.Minute, UsageUnit.Hour, UsageUnit.Day, UsageUnit.Week, UsageUnit.Month, UsageUnit.Year];

        /// <summary>
        /// 하위 단위에서 모아 만드는 단위(재계산 순서: 하위가 먼저 최신이 된다).
        /// </summary>
        public static IReadOnlyList<UsageUnit> RolledUp { get; } =
            [UsageUnit.Hour, UsageUnit.Day, UsageUnit.Week, UsageUnit.Month, UsageUnit.Year];

        /// <summary>
        /// 이 단위를 만들 때 모으는 하위 단위. 분은 하위가 없다.
        /// </summary>
        public static UsageUnit? Child(this UsageUnit unit) => unit switch
        {
            UsageUnit.Minute => null,
            UsageUnit.Hour => UsageUnit.Minute,
            UsageUnit.Day => UsageUnit.Hour,
            UsageUnit.Week => UsageUnit.Day,
            UsageUnit.Month => UsageUnit.Day,
            UsageUnit.Year => UsageUnit.Month,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
        };

        /// <summary>
        /// 이 단위를 직접 모으는 상위 단위. 일은 주·월 둘이고, 주·년은 없다.
        /// </summary>
        public static IReadOnlyList<UsageUnit> DirectParents(this UsageUnit unit) => unit switch
        {
            UsageUnit.Minute => [UsageUnit.Hour],
            UsageUnit.Hour => [UsageUnit.Day],
            UsageUnit.Day => [UsageUnit.Week, UsageUnit.Month],
            UsageUnit.Month => [UsageUnit.Year],
            UsageUnit.Week or UsageUnit.Year => [],
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
        };

        /// <summary>
        /// 저장·API에 쓰는 소문자 이름.
        /// </summary>
        public static string ToName(this UsageUnit unit) => unit switch
        {
            UsageUnit.Minute => "minute",
            UsageUnit.Hour => "hour",
            UsageUnit.Day => "day",
            UsageUnit.Week => "week",
            UsageUnit.Month => "month",
            UsageUnit.Year => "year",
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
        };

        /// <summary>
        /// 소문자 이름을 단위로 바꾼다. 정해진 이름이 아니면 false.
        /// </summary>
        public static bool TryParse(string? name, out UsageUnit unit)
        {
            foreach (UsageUnit candidate in All)
            {
                if (string.Equals(candidate.ToName(), name, StringComparison.Ordinal))
                {
                    unit = candidate;
                    return true;
                }
            }
            unit = default;
            return false;
        }
    }
}
