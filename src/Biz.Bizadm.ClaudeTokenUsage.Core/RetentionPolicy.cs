namespace Biz.Bizadm.ClaudeTokenUsage.Core
{
    /// <summary>
    /// 단위별 보관 기간: 분 24시간, 시 7일, 일 1개월, 주 1년, 월 5년, 년 영구.
    /// </summary>
    public static class RetentionPolicy
    {
        /// <summary>
        /// 보관 경계. 구간 시작이 이 시각보다 이른 행을 지운다. 현재 구간 시작 − 보관 기간이며 년은 null(영구)이다.
        /// 분·시는 고정 길이로, 일·주·월은 달력 기준으로 빼고, 그 달에 없는 날짜는 말일로 맞춘다(<see cref="DateTime.AddMonths"/>).
        /// </summary>
        public static DateTime? Cutoff(UsageUnit unit, DateTime nowKst)
        {
            DateTime current = BucketCalculator.Floor(unit, nowKst);
            return unit switch
            {
                UsageUnit.Minute => current.AddHours(-24),
                UsageUnit.Hour => current.AddDays(-7),
                UsageUnit.Day => current.AddMonths(-1),
                UsageUnit.Week => current.AddYears(-1),
                UsageUnit.Month => current.AddYears(-5),
                UsageUnit.Year => null,
                _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
            };
        }

        /// <summary>
        /// 보관 하한: 보관 경계 이상인 첫 구간 시작. 주의 보관 경계는 대개 월요일이 아니므로 올려서 쓴다. 년은 null이다.
        /// </summary>
        public static DateTime? RetainedFrom(UsageUnit unit, DateTime nowKst)
        {
            DateTime? cutoff = Cutoff(unit, nowKst);
            return cutoff is null ? null : BucketCalculator.Ceil(unit, cutoff.Value);
        }

        /// <summary>
        /// 구간 시작이 보관 기간 안인지.
        /// </summary>
        public static bool IsRetained(UsageUnit unit, DateTime bucketStart, DateTime nowKst)
        {
            DateTime? cutoff = Cutoff(unit, nowKst);
            return cutoff is null || bucketStart >= cutoff.Value;
        }
    }
}
