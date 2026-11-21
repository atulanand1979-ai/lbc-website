using Lbc.Core.Abstractions;
using Lbc.Core.Domain;

namespace Lbc.Core.Services;

public sealed record VenueInput(string? Slug, string? City, string? Name, string? Address, string? Postcode, string? MapUrl, bool IsActive, string? StartsOn, int SortOrder);

public sealed record ClassInput(string? DayOfWeek, string? StartTime, string? EndTime, string? Level, bool IsActive, IReadOnlyList<string>? SkipDates);

/// <summary>Admin management of places and their weekly class schedules.</summary>
public sealed class ScheduleAdminService(IVenueRepository venues, IClassRepository classes)
{
    public async Task<Venue> CreateVenueAsync(VenueInput input, CancellationToken ct = default)
    {
        var slug = Guard.Slug(string.IsNullOrWhiteSpace(input.Slug) ? ToSlug(input.City) : input.Slug);
        if (await venues.GetAsync(slug, ct) is not null) throw new ValidationException($"A location with the key '{slug}' already exists.");
        var venue = new Venue { Slug = slug, City = "", Name = "" };
        Apply(venue, input);
        await venues.UpsertAsync(venue, ct);
        return venue;
    }

    public async Task<Venue> UpdateVenueAsync(string slug, VenueInput input, CancellationToken ct = default)
    {
        var venue = await venues.GetAsync(slug, ct) ?? throw new NotFoundException("Location not found.");
        Apply(venue, input);
        await venues.UpsertAsync(venue, ct);
        return venue;
    }

    /// <summary>Only allowed once the venue has no classes; otherwise deactivate it so history stays intact.</summary>
    public async Task DeleteVenueAsync(string slug, CancellationToken ct = default)
    {
        if ((await classes.GetByVenueAsync(slug, ct)).Count > 0)
            throw new ValidationException("Remove this location's classes first, or mark the location inactive instead.");
        await venues.DeleteAsync(slug, ct);
    }

    public async Task<ClassSchedule> CreateClassAsync(string venueSlug, ClassInput input, CancellationToken ct = default)
    {
        _ = await venues.GetAsync(venueSlug, ct) ?? throw new NotFoundException("Location not found.");
        var schedule = new ClassSchedule { Id = Ids.New(), VenueSlug = venueSlug };
        Apply(schedule, input);
        await classes.UpsertAsync(schedule, ct);
        return schedule;
    }

    public async Task<ClassSchedule> UpdateClassAsync(string venueSlug, string classId, ClassInput input, CancellationToken ct = default)
    {
        var schedule = await classes.GetAsync(venueSlug, classId, ct) ?? throw new NotFoundException("Class not found.");
        Apply(schedule, input);
        await classes.UpsertAsync(schedule, ct);
        return schedule;
    }

    /// <summary>Existing bookings keep their own copy of the class label, so deleting a class never loses history.</summary>
    public Task DeleteClassAsync(string venueSlug, string classId, CancellationToken ct = default) =>
        classes.DeleteAsync(venueSlug, classId, ct);

    private static void Apply(Venue v, VenueInput i)
    {
        v.City = Guard.Required(i.City, "City", 60);
        v.Name = Guard.Required(i.Name, "Venue name", 120);
        v.Address = Guard.Optional(i.Address, "Address", 200);
        v.Postcode = Guard.Optional(i.Postcode, "Postcode", 10).ToUpperInvariant();
        var map = Guard.Optional(i.MapUrl, "Map link", 500);
        if (map.Length > 0 && !(Uri.TryCreate(map, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
            throw new ValidationException("Map link must be an https:// address.");
        v.MapUrl = map.Length > 0 ? map : null;
        v.IsActive = i.IsActive;
        v.SortOrder = i.SortOrder;
        if (string.IsNullOrWhiteSpace(i.StartsOn)) v.StartsOn = null;
        else if (DateOnly.TryParseExact(i.StartsOn, "yyyy-MM-dd", out var d)) v.StartsOn = d;
        else throw new ValidationException("'Classes start on' must be a valid date.");
    }

    private static void Apply(ClassSchedule c, ClassInput i)
    {
        if (!Enum.TryParse<DayOfWeek>(i.DayOfWeek, ignoreCase: true, out var day) || !Enum.IsDefined(day))
            throw new ValidationException("Please choose the day of the week.");
        if (!TimeOnly.TryParseExact(i.StartTime ?? "", "HH:mm", out var start)) throw new ValidationException("Please enter a start time (HH:mm).");
        if (!TimeOnly.TryParseExact(i.EndTime ?? "", "HH:mm", out var end)) throw new ValidationException("Please enter an end time (HH:mm).");
        if (end <= start) throw new ValidationException("End time must be after the start time.");
        if (!Enum.TryParse<ClassLevel>(i.Level, ignoreCase: true, out var level) || !Enum.IsDefined(level))
            throw new ValidationException("Please choose the class level.");

        var skip = new List<DateOnly>();
        foreach (var s in i.SkipDates ?? [])
        {
            if (!DateOnly.TryParseExact(s, "yyyy-MM-dd", out var d)) throw new ValidationException($"'{s}' is not a valid skip date.");
            if (d.DayOfWeek != day) throw new ValidationException($"{d:ddd d MMM yyyy} is not a {day}.");
            skip.Add(d);
        }

        c.DayOfWeek = day;
        c.StartTime = start;
        c.EndTime = end;
        c.Level = level;
        c.IsActive = i.IsActive;
        c.SkipDates = skip.Distinct().Order().ToList();
    }

    private static string ToSlug(string? city) =>
        new string((city ?? "").Trim().ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray())
            .Replace("--", "-").Trim('-');
}
