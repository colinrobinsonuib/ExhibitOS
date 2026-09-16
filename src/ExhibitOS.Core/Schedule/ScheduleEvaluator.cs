using ExhibitOS.Core.Configuration;

namespace ExhibitOS.Core.Schedule;

public class ScheduleEvaluator
{
    private readonly ScheduleConfig _schedule;
    private readonly TimeZoneInfo _timeZone;

    public ScheduleEvaluator(ScheduleConfig schedule)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        try
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            _timeZone = TimeZoneInfo.Local;
        }
    }

    public static TimeOnly ParseTime(string timeString, TimeOnly defaultTime)
    {
        return TimeOnly.TryParse(timeString, out var parsed) ? parsed : defaultTime;
    }

    public TimeOnly OpeningTime => ParseTime(_schedule.OpeningTime, new TimeOnly(7, 0));
    public TimeOnly ClosingTime => ParseTime(_schedule.ClosingTime, new TimeOnly(20, 0));
    public TimeOnly MorningRebootTime => ParseTime(_schedule.MorningRebootTime, new TimeOnly(6, 45));

    public DateTime GetLocalTime(DateTimeOffset utcNow)
    {
        return TimeZoneInfo.ConvertTimeFromUtc(utcNow.UtcDateTime, _timeZone);
    }

    public bool IsExhibitionOpen(DateTimeOffset utcNow)
    {
        var local = GetLocalTime(utcNow);
        var currentTime = TimeOnly.FromDateTime(local);

        if (OpeningTime < ClosingTime)
        {
            // Standard day schedule, e.g. 07:00 to 20:00
            return currentTime >= OpeningTime && currentTime < ClosingTime;
        }
        else
        {
            // Overnight schedule crossing midnight, e.g. 18:00 to 02:00
            return currentTime >= OpeningTime || currentTime < ClosingTime;
        }
    }

    public TimeSpan GetTimeUntilClosing(DateTimeOffset utcNow)
    {
        var local = GetLocalTime(utcNow);
        var currentDate = DateOnly.FromDateTime(local);
        var closingDateTime = currentDate.ToDateTime(ClosingTime);

        if (local > closingDateTime)
        {
            closingDateTime = currentDate.AddDays(1).ToDateTime(ClosingTime);
        }

        return closingDateTime - local;
    }

    public TimeSpan GetTimeUntilOpening(DateTimeOffset utcNow)
    {
        var local = GetLocalTime(utcNow);
        var currentDate = DateOnly.FromDateTime(local);
        var openingDateTime = currentDate.ToDateTime(OpeningTime);

        if (local > openingDateTime)
        {
            openingDateTime = currentDate.AddDays(1).ToDateTime(OpeningTime);
        }

        return openingDateTime - local;
    }

    public bool IsMorningRebootMissed(DateTimeOffset utcNow, DateTimeOffset lastBootTimeUtc)
    {
        var currentLocal = GetLocalTime(utcNow);
        var bootLocal = GetLocalTime(lastBootTimeUtc);

        var todayDate = DateOnly.FromDateTime(currentLocal);
        var todayRebootDateTime = todayDate.ToDateTime(MorningRebootTime);

        // If current time is past today's scheduled reboot
        if (currentLocal > todayRebootDateTime)
        {
            // If system booted before today's scheduled reboot, today's reboot was missed
            if (bootLocal < todayRebootDateTime)
            {
                return true;
            }
        }

        return false;
    }
}
