namespace Lbc.Core.Services;

/// <summary>Classes run in England, so "today" and class start times are UK local time.</summary>
public static class UkTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Europe/London", out var tz)) return tz;
        if (TimeZoneInfo.TryFindSystemTimeZoneById("GMT Standard Time", out tz)) return tz;
        return TimeZoneInfo.Utc;
    }

    public static DateTime Now(TimeProvider clock) =>
        TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime;

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(Now(clock));
}
