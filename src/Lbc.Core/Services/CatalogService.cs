using Lbc.Core.Abstractions;
using Lbc.Core.Domain;
using Microsoft.Extensions.Options;

namespace Lbc.Core.Services;

public sealed record ClassDto(string Id, string DayOfWeek, string StartTime, string EndTime, string Level, string Label, bool IsActive, IReadOnlyList<string> SkipDates);

public sealed record VenueDto(
    string Slug, string City, string Name, string Address, string Postcode, string? MapUrl,
    bool IsActive, string? StartsOn, int SortOrder, IReadOnlyList<ClassDto> Classes);

public sealed record PricesDto(int PayAsYouGoPence, int BronzePence, int SilverPence, int GoldPence);

public sealed record UpcomingDateDto(string Date, string Label);

/// <summary>Public, read-only view of places and their weekly classes.</summary>
public sealed class CatalogService(
    IVenueRepository venues,
    IClassRepository classes,
    IOptions<PricingOptions> pricing,
    IOptions<SiteOptions> site,
    TimeProvider clock)
{
    public PricesDto Prices()
    {
        var p = pricing.Value;
        return new(p.PayAsYouGoPence, p.BronzePence, p.SilverPence, p.GoldPence);
    }

    public async Task<IReadOnlyList<VenueDto>> GetVenuesAsync(bool includeInactive, CancellationToken ct = default)
    {
        var allVenues = await venues.GetAllAsync(ct);
        var byVenue = (await classes.GetAllAsync(ct)).ToLookup(c => c.VenueSlug);
        return allVenues
            .Where(v => includeInactive || v.IsActive)
            .OrderBy(v => v.SortOrder).ThenBy(v => v.City)
            .Select(v => ToDto(v, byVenue[v.Slug].Where(c => includeInactive || c.IsActive)))
            .ToList();
    }

    public async Task<IReadOnlyList<UpcomingDateDto>> GetUpcomingDatesAsync(string venueSlug, string classId, CancellationToken ct = default)
    {
        var venue = await venues.GetAsync(venueSlug, ct) ?? throw new NotFoundException("Location not found.");
        var schedule = await classes.GetAsync(venueSlug, classId, ct) ?? throw new NotFoundException("Class not found.");
        return ScheduleCalculator.UpcomingDates(venue, schedule, UkTime.Now(clock), site.Value.BookableWeeksAhead)
            .Select(d => new UpcomingDateDto(d.ToString("yyyy-MM-dd"), d.ToString("dddd d MMMM yyyy")))
            .ToList();
    }

    public static VenueDto ToDto(Venue v, IEnumerable<ClassSchedule> cs) => new(
        v.Slug, v.City, v.Name, v.Address, v.Postcode, v.MapUrl, v.IsActive,
        v.StartsOn?.ToString("yyyy-MM-dd"), v.SortOrder,
        cs.OrderBy(c => ((int)c.DayOfWeek + 6) % 7).ThenBy(c => c.StartTime).Select(ToDto).ToList());

    public static ClassDto ToDto(ClassSchedule c) => new(
        c.Id, c.DayOfWeek.ToString(), c.StartTime.ToString("HH:mm"), c.EndTime.ToString("HH:mm"),
        c.Level.ToString(), ScheduleCalculator.Describe(c), c.IsActive,
        c.SkipDates.Order().Select(d => d.ToString("yyyy-MM-dd")).ToList());
}
