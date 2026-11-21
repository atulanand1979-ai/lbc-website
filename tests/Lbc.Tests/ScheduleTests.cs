using Lbc.Core.Domain;
using Lbc.Core.Services;

namespace Lbc.Tests;

public class ScheduleTests
{
    private static Venue V(DateOnly? startsOn = null, bool active = true) => new() { Slug = "x", City = "X", Name = "X", StartsOn = startsOn, IsActive = active };
    private static ClassSchedule C(DayOfWeek day, int hour = 19, params DateOnly[] skip) =>
        new() { Id = "c", VenueSlug = "x", DayOfWeek = day, StartTime = new(hour, 0), EndTime = new(hour + 1, 0), SkipDates = skip.ToList() };

    [Fact]
    public void Every_monday_returns_next_mondays()
    {
        var now = new DateTime(2026, 12, 2, 9, 0, 0); // Wednesday
        var dates = ScheduleCalculator.UpcomingDates(V(), C(DayOfWeek.Monday), now, 3);
        Assert.Equal([new(2026, 12, 7), new(2026, 12, 14), new(2026, 12, 21)], dates);
    }

    [Fact]
    public void Today_included_before_class_starts_but_not_after()
    {
        var tuesday = new DateOnly(2026, 12, 1);
        var before = ScheduleCalculator.UpcomingDates(V(), C(DayOfWeek.Tuesday), tuesday.ToDateTime(new TimeOnly(18, 59)), 2);
        var after = ScheduleCalculator.UpcomingDates(V(), C(DayOfWeek.Tuesday), tuesday.ToDateTime(new TimeOnly(19, 0)), 2);
        Assert.Equal(tuesday, before[0]);
        Assert.Equal(tuesday.AddDays(7), after[0]);
    }

    [Fact]
    public void Skip_dates_are_excluded()
    {
        var now = new DateTime(2026, 12, 2, 9, 0, 0);
        var dates = ScheduleCalculator.UpcomingDates(V(), C(DayOfWeek.Monday, 19, new DateOnly(2026, 12, 14)), now, 3);
        Assert.Equal([new(2026, 12, 7), new(2026, 12, 21)], dates);
    }

    [Fact]
    public void Venue_start_date_pushes_first_date_forward()
    {
        var now = new DateTime(2026, 12, 2, 9, 0, 0);
        var dates = ScheduleCalculator.UpcomingDates(V(new DateOnly(2027, 2, 1)), C(DayOfWeek.Thursday), now, 2);
        Assert.Equal([new(2027, 2, 4), new(2027, 2, 11)], dates);
    }

    [Fact]
    public void Inactive_venue_or_class_has_no_dates()
    {
        var now = new DateTime(2026, 12, 2, 9, 0, 0);
        Assert.Empty(ScheduleCalculator.UpcomingDates(V(active: false), C(DayOfWeek.Monday), now, 4));
        var c = C(DayOfWeek.Monday); c.IsActive = false;
        Assert.Empty(ScheduleCalculator.UpcomingDates(V(), c, now, 4));
    }

    [Fact]
    public void Describe_formats_times()
    {
        var c = C(DayOfWeek.Tuesday, 20);
        c.Level = ClassLevel.Intermediate;
        Assert.Equal("Tuesday 8pm–9pm Intermediate", ScheduleCalculator.Describe(c));
    }

    [Fact]
    public async Task Admin_rejects_skip_date_on_wrong_weekday()
    {
        var app = new TestApp();
        await app.AddLeicesterTuesdayAsync();
        await Assert.ThrowsAsync<ValidationException>(() =>
            app.Schedule.CreateClassAsync("leicester", new ClassInput("Monday", "19:00", "20:00", "Beginners", true, ["2026-12-08"])));
    }

    [Fact]
    public async Task Admin_added_weekly_class_shows_upcoming_dates()
    {
        var app = new TestApp();
        var (_, cls) = await app.AddLeicesterTuesdayAsync();
        var dates = await app.Catalog.GetUpcomingDatesAsync("leicester", cls.Id);
        Assert.Equal(8, dates.Count);
        Assert.Equal("2026-12-01", dates[0].Date); // today, before 7pm
        Assert.All(dates, d => Assert.Equal(DayOfWeek.Tuesday, DateOnly.Parse(d.Date).DayOfWeek));
    }
}
