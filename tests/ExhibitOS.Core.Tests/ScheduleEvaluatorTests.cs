using ExhibitOS.Core.Configuration;
using ExhibitOS.Core.Schedule;
using Xunit;

namespace ExhibitOS.Core.Tests;

public class ScheduleEvaluatorTests
{
    [Fact]
    public void IsExhibitionOpen_StandardDay_EvaluatesCorrectly()
    {
        var schedule = new ScheduleConfig
        {
            OpeningTime = "07:00",
            ClosingTime = "20:00",
            TimeZoneId = "UTC"
        };
        var evaluator = new ScheduleEvaluator(schedule);

        // 06:59:59 is closed
        var beforeOpen = new DateTimeOffset(2026, 9, 16, 6, 59, 59, TimeSpan.Zero);
        Assert.False(evaluator.IsExhibitionOpen(beforeOpen));

        // 07:00:00 is open
        var atOpen = new DateTimeOffset(2026, 9, 16, 7, 0, 0, TimeSpan.Zero);
        Assert.True(evaluator.IsExhibitionOpen(atOpen));

        // 12:00:00 is open
        var midday = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        Assert.True(evaluator.IsExhibitionOpen(midday));

        // 20:00:00 is closed
        var atClose = new DateTimeOffset(2026, 9, 16, 20, 0, 0, TimeSpan.Zero);
        Assert.False(evaluator.IsExhibitionOpen(atClose));
    }

    [Fact]
    public void IsExhibitionOpen_OvernightSchedule_EvaluatesCorrectly()
    {
        var schedule = new ScheduleConfig
        {
            OpeningTime = "18:00",
            ClosingTime = "02:00",
            TimeZoneId = "UTC"
        };
        var evaluator = new ScheduleEvaluator(schedule);

        // 17:59:59 is closed
        var beforeOpen = new DateTimeOffset(2026, 9, 16, 17, 59, 59, TimeSpan.Zero);
        Assert.False(evaluator.IsExhibitionOpen(beforeOpen));

        // 23:00:00 is open
        var night = new DateTimeOffset(2026, 9, 16, 23, 0, 0, TimeSpan.Zero);
        Assert.True(evaluator.IsExhibitionOpen(night));

        // 01:30:00 next day is open
        var morning = new DateTimeOffset(2026, 9, 17, 1, 30, 0, TimeSpan.Zero);
        Assert.True(evaluator.IsExhibitionOpen(morning));

        // 02:00:00 is closed
        var closed = new DateTimeOffset(2026, 9, 17, 2, 0, 0, TimeSpan.Zero);
        Assert.False(evaluator.IsExhibitionOpen(closed));
    }

    [Fact]
    public void IsMorningRebootMissed_DetectsMissedRebootOnWake()
    {
        var schedule = new ScheduleConfig
        {
            MorningRebootTime = "06:45",
            TimeZoneId = "UTC"
        };
        var evaluator = new ScheduleEvaluator(schedule);

        // Last boot was yesterday at 06:45
        var lastBoot = new DateTimeOffset(2026, 9, 15, 6, 45, 0, TimeSpan.Zero);

        // Technician woke PC manually at 08:00 (past today's scheduled 06:45 reboot)
        var wakeTime = new DateTimeOffset(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);

        Assert.True(evaluator.IsMorningRebootMissed(wakeTime, lastBoot));

        // But if boot was today at 06:45, wake at 08:00 is not missed
        var todayBoot = new DateTimeOffset(2026, 9, 16, 6, 45, 10, TimeSpan.Zero);
        Assert.False(evaluator.IsMorningRebootMissed(wakeTime, todayBoot));
    }
}
