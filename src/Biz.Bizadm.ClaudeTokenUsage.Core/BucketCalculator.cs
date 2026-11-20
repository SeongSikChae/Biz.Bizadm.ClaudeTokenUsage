namespace Biz.Bizadm.ClaudeTokenUsage.Core
{
    /// <summary>
    /// KST 벽시계 시각의 구간 경계 계산. 일은 00:00, 주는 월요일 00:00, 월은 1일, 년은 1월 1일에 시작한다.
    /// </summary>
    public static class BucketCalculator
    {
        /// <summary>
        /// 그 시각이 속한 구간의 시작.
        /// </summary>
        public static DateTime Floor(UsageUnit unit, DateTime kst) => unit switch
        {
            UsageUnit.Minute => new DateTime(kst.Year, kst.Month, kst.Day, kst.Hour, kst.Minute, 0),
            UsageUnit.Hour => new DateTime(kst.Year, kst.Month, kst.Day, kst.Hour, 0, 0),
            UsageUnit.Day => kst.Date,
            UsageUnit.Week => kst.Date.AddDays(-(((int)kst.DayOfWeek + 6) % 7)),
            UsageUnit.Month => new DateTime(kst.Year, kst.Month, 1),
            UsageUnit.Year => new DateTime(kst.Year, 1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
        };

        /// <summary>
        /// 그 시각 이상인 첫 구간 시작. 시각이 경계이면 그대로다.
        /// </summary>
        public static DateTime Ceil(UsageUnit unit, DateTime kst)
        {
            DateTime floor = Floor(unit, kst);
            return floor == kst ? kst : Next(unit, floor);
        }

        /// <summary>
        /// 구간 시작의 다음 구간 시작.
        /// </summary>
        public static DateTime Next(UsageUnit unit, DateTime start) => unit switch
        {
            UsageUnit.Minute => start.AddMinutes(1),
            UsageUnit.Hour => start.AddHours(1),
            UsageUnit.Day => start.AddDays(1),
            UsageUnit.Week => start.AddDays(7),
            UsageUnit.Month => start.AddMonths(1),
            UsageUnit.Year => start.AddYears(1),
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
        };
    }
}
