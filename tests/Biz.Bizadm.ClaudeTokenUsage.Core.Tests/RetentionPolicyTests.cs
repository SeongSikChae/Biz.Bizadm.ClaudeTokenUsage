namespace Biz.Bizadm.ClaudeTokenUsage.Core.Tests;

[TestClass]
public sealed class RetentionPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 14, 37, 12);

    [TestMethod]
    public void Cutoff_Minute_Is24HoursBeforeCurrentMinute()
        => Assert.AreEqual(new DateTime(2026, 11, 19, 14, 37, 0), RetentionPolicy.Cutoff(UsageUnit.Minute, Now));

    [TestMethod]
    public void Cutoff_Hour_Is7DaysBeforeCurrentHour()
        => Assert.AreEqual(new DateTime(2026, 11, 13, 14, 0, 0), RetentionPolicy.Cutoff(UsageUnit.Hour, Now));

    [TestMethod]
    public void Cutoff_Day_IsOneCalendarMonthBefore()
        => Assert.AreEqual(new DateTime(2026, 10, 20), RetentionPolicy.Cutoff(UsageUnit.Day, Now));

    [TestMethod]
    public void Cutoff_Day_MonthEnd_ClampsToLastDay()
        => Assert.AreEqual(new DateTime(2026, 2, 28), RetentionPolicy.Cutoff(UsageUnit.Day, new DateTime(2026, 3, 31, 10, 0, 0)));

    [TestMethod]
    public void Cutoff_Week_LeapDay_ClampsToFebruary28()
    {
        // 2028-02-29는 화요일이라 그 주 월요일은 02-28이고, 1년 전은 2027-02-28이다.
        Assert.AreEqual(new DateTime(2027, 2, 28), RetentionPolicy.Cutoff(UsageUnit.Week, new DateTime(2028, 2, 29, 9, 0, 0)));
        // 월 단위: 2028-02-01 − 5년 = 2023-02-01
        Assert.AreEqual(new DateTime(2023, 2, 1), RetentionPolicy.Cutoff(UsageUnit.Month, new DateTime(2028, 2, 29, 9, 0, 0)));
    }

    [TestMethod]
    public void Cutoff_Year_IsNull()
        => Assert.IsNull(RetentionPolicy.Cutoff(UsageUnit.Year, Now));

    [TestMethod]
    public void RetainedFrom_Week_IsMonday()
    {
        // 이번 주 월요일 11-16 − 1년 = 2025-11-16(일요일) → 올려서 2025-11-17(월요일)
        DateTime? retainedFrom = RetentionPolicy.RetainedFrom(UsageUnit.Week, Now);
        Assert.AreEqual(new DateTime(2025, 11, 17), retainedFrom);
        Assert.AreEqual(DayOfWeek.Monday, retainedFrom!.Value.DayOfWeek);
    }

    [TestMethod]
    public void RetainedFrom_NonWeekUnits_EqualsCutoff()
    {
        foreach (UsageUnit unit in new[] { UsageUnit.Minute, UsageUnit.Hour, UsageUnit.Day, UsageUnit.Month })
            Assert.AreEqual(RetentionPolicy.Cutoff(unit, Now), RetentionPolicy.RetainedFrom(unit, Now), unit.ToString());
        Assert.IsNull(RetentionPolicy.RetainedFrom(UsageUnit.Year, Now));
    }

    [TestMethod]
    public void IsRetained_BoundaryIncluded()
    {
        Assert.IsTrue(RetentionPolicy.IsRetained(UsageUnit.Day, new DateTime(2026, 10, 20), Now));
        Assert.IsFalse(RetentionPolicy.IsRetained(UsageUnit.Day, new DateTime(2026, 10, 19), Now));
        Assert.IsTrue(RetentionPolicy.IsRetained(UsageUnit.Year, new DateTime(1990, 1, 1), Now));
    }
}
