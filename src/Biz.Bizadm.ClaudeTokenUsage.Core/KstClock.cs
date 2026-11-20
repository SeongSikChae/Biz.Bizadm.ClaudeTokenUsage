namespace Biz.Bizadm.ClaudeTokenUsage.Core
{
    /// <summary>
    /// 한국 표준시(UTC+9) 변환. 한국은 일광 절약 시간이 없으므로 고정 오프셋으로 계산하고 OS 시간대 데이터에 의존하지 않는다.
    /// </summary>
    public static class KstClock
    {
        public static readonly TimeSpan Offset = TimeSpan.FromHours(9);

        /// <summary>
        /// 시각을 KST 벽시계 시각(<see cref="DateTimeKind.Unspecified"/>)으로 바꾼다.
        /// </summary>
        public static DateTime ToKst(DateTimeOffset time) => DateTime.SpecifyKind(time.ToOffset(Offset).DateTime, DateTimeKind.Unspecified);

        /// <summary>
        /// KST 벽시계 시각을 오프셋이 있는 시각으로 바꾼다.
        /// </summary>
        public static DateTimeOffset FromKst(DateTime kst) => new(DateTime.SpecifyKind(kst, DateTimeKind.Unspecified), Offset);

        /// <summary>
        /// 지금 시각을 KST 벽시계로 돌려준다.
        /// </summary>
        public static DateTime Now(TimeProvider timeProvider) => ToKst(timeProvider.GetUtcNow());
    }
}
