namespace Biz.Bizadm.ClaudeTokenUsage.Core.Tests;

[TestClass]
public sealed class BucketCalculatorTests
{
    private static DateTime Kst(int y, int mo, int d, int h = 0, int mi = 0, int s = 0) => new(y, mo, d, h, mi, s);

    [TestMethod]
    public void ToKst_UtcBeforeAndAtKstMidnight_SplitsDay()
    {
        DateTime before = KstClock.ToKst(new DateTimeOffset(2026, 11, 20, 14, 59, 59, TimeSpan.Zero));
        DateTime at = KstClock.ToKst(new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero));

        Assert.AreEqual(Kst(2026, 11, 20), BucketCalculator.Floor(UsageUnit.Day, before));
        Assert.AreEqual(Kst(2026, 11, 21), BucketCalculator.Floor(UsageUnit.Day, at));
    }

    [TestMethod]
    public void Floor_MinuteAndHour_TruncatesSmallerParts()
    {
        DateTime t = Kst(2026, 11, 20, 14, 37, 12).AddMilliseconds(345);
        Assert.AreEqual(Kst(2026, 11, 20, 14, 37), BucketCalculator.Floor(UsageUnit.Minute, t));
        Assert.AreEqual(Kst(2026, 11, 20, 14), BucketCalculator.Floor(UsageUnit.Hour, t));
    }

    [TestMethod]
    [DataRow(2026, 11, 22, 2026, 11, 16)] // 일요일 → 그 주 월요일
    [DataRow(2026, 11, 23, 2026, 11, 23)] // 월요일 그대로
    [DataRow(2026, 11, 21, 2026, 11, 16)] // 토요일
    [DataRow(2027, 1, 1, 2026, 12, 28)]   // 연도를 넘는 주
    public void Floor_Week_StartsOnMonday(int y, int mo, int d, int ey, int emo, int ed)
    {
        DateTime start = BucketCalculator.Floor(UsageUnit.Week, Kst(y, mo, d, 23, 59));
        Assert.AreEqual(Kst(ey, emo, ed), start);
        Assert.AreEqual(DayOfWeek.Monday, start.DayOfWeek);
    }

    [TestMethod]
    public void Floor_MonthAndYear_AtMonthEndAndYearEnd()
    {
        DateTime t = Kst(2026, 12, 31, 23, 59, 59);
        Assert.AreEqual(Kst(2026, 12, 1), BucketCalculator.Floor(UsageUnit.Month, t));
        Assert.AreEqual(Kst(2026, 1, 1), BucketCalculator.Floor(UsageUnit.Year, t));
    }

    [TestMethod]
    public void Next_MonthFromLeapFebruary_GoesToMarch()
    {
        Assert.AreEqual(Kst(2028, 3, 1), BucketCalculator.Next(UsageUnit.Month, Kst(2028, 2, 1)));
        Assert.AreEqual(Kst(2028, 3, 1), BucketCalculator.Next(UsageUnit.Day, Kst(2028, 2, 29)));
        Assert.AreEqual(Kst(2026, 11, 30), BucketCalculator.Next(UsageUnit.Week, Kst(2026, 11, 23)));
    }

    [TestMethod]
    public void Ceil_OnBoundary_ReturnsSame()
    {
        Assert.AreEqual(Kst(2026, 11, 23), BucketCalculator.Ceil(UsageUnit.Week, Kst(2026, 11, 23)));
        Assert.AreEqual(Kst(2026, 11, 20, 15), BucketCalculator.Ceil(UsageUnit.Hour, Kst(2026, 11, 20, 15)));
    }

    [TestMethod]
    public void Ceil_OffBoundary_ReturnsNextBoundary()
    {
        Assert.AreEqual(Kst(2026, 11, 23), BucketCalculator.Ceil(UsageUnit.Week, Kst(2026, 11, 18)));
        Assert.AreEqual(Kst(2026, 11, 20, 16), BucketCalculator.Ceil(UsageUnit.Hour, Kst(2026, 11, 20, 15, 0, 1)));
        Assert.AreEqual(Kst(2027, 1, 1), BucketCalculator.Ceil(UsageUnit.Year, Kst(2026, 1, 2)));
    }

    [TestMethod]
    public void UsageUnit_ChildAndDirectParents()
    {
        Assert.IsNull(UsageUnit.Minute.Child());
        Assert.AreEqual(UsageUnit.Day, UsageUnit.Week.Child());
        Assert.AreEqual(UsageUnit.Day, UsageUnit.Month.Child());
        CollectionAssert.AreEqual(new[] { UsageUnit.Week, UsageUnit.Month }, UsageUnit.Day.DirectParents().ToArray());
        Assert.IsEmpty(UsageUnit.Week.DirectParents());
        Assert.IsTrue(UsageUnitExtensions.TryParse("week", out UsageUnit unit));
        Assert.AreEqual(UsageUnit.Week, unit);
        Assert.IsFalse(UsageUnitExtensions.TryParse("Week", out _));
    }
}
