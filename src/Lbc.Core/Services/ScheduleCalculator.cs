using Lbc.Core.Domain;

namespace Lbc.Core.Services;

/// <summary>Turns a weekly class ("every Monday 7pm") into concrete bookable dates.</summary>
public static class ScheduleCalculator
{
    /// <summary>
    /// Upcoming dates the class can be booked for, starting from <paramref name="now"/> (UK local time).
    /// Today is included only if the class hasn't started yet.
    /// </summary>
    public static IReadOnlyList<DateOnly> UpcomingDates(Venue venue, ClassSchedule schedule, DateTime now, int weeksAhead)
    {
        if (!venue.IsActive || !schedule.IsActive || weeksAhead <= 0) return [];

        var today = DateOnly.FromDateTime(now);
        var first = today.AddDays(((int)schedule.DayOfWeek - (int)today.DayOfWeek + 7) % 7);
        if (first == today && TimeOnly.FromDateTime(now) >= schedule.StartTime) first = first.AddDays(7);
        if (venue.StartsOn is { } startsOn && first < startsOn)
            first = startsOn.AddDays(((int)schedule.DayOfWeek - (int)startsOn.DayOfWeek + 7) % 7);

        var horizon = (venue.StartsOn is { } s && s > today ? s : today).AddDays(7 * weeksAhead);
        var skip = schedule.SkipDates.ToHashSet();
        var dates = new List<DateOnly>();
        for (var d = first; d < horizon; d = d.AddDays(7))
            if (!skip.Contains(d)) dates.Add(d);
        return dates;
    }

    public static bool IsBookable(Venue venue, ClassSchedule schedule, DateOnly date, DateTime now, int weeksAhead) =>
        UpcomingDates(venue, schedule, now, weeksAhead).Contains(date);

    public static string Describe(ClassSchedule schedule) =>
        $"{schedule.DayOfWeek} {FormatTime(schedule.StartTime)}–{FormatTime(schedule.EndTime)} {schedule.Level}";

    public static string FormatTime(TimeOnly t) =>
        t.Minute == 0 ? $"{(t.Hour % 12 == 0 ? 12 : t.Hour % 12)}{(t.Hour < 12 ? "am" : "pm")}"
                      : $"{(t.Hour % 12 == 0 ? 12 : t.Hour % 12)}:{t.Minute:00}{(t.Hour < 12 ? "am" : "pm")}";
}
