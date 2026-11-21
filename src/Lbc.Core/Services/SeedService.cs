using Lbc.Core.Abstractions;
using Lbc.Core.Domain;

namespace Lbc.Core.Services;

/// <summary>Loads the venues and weekly classes from the client's content document into an empty database.</summary>
public sealed class SeedService(IVenueRepository venues, IClassRepository classes)
{
    private sealed record SeedClass(DayOfWeek Day, int StartHour, ClassLevel Level);
    private sealed record SeedVenue(string Slug, string City, string Name, string Address, string Postcode, DateOnly? StartsOn, SeedClass[] Classes);

    private static readonly SeedVenue[] Data =
    [
        new("bedford", "Bedford", "Addison Centre", "Bedford Rd, Kempston", "MK42 8PN", null,
            [new(DayOfWeek.Monday, 19, ClassLevel.Beginners)]),
        new("northampton", "Northampton", "Northampton High School", "Hardingstone", "NN4 6UU", null,
            [new(DayOfWeek.Monday, 19, ClassLevel.Beginners)]),
        new("leicester", "Leicester", "Avanti Fields School", "21 Furrow View", "LE5 0BX", null,
            [new(DayOfWeek.Tuesday, 19, ClassLevel.Beginners), new(DayOfWeek.Tuesday, 20, ClassLevel.Intermediate)]),
        new("nottingham", "Nottingham", "The Vine Community Centre", "Abbey Rd", "NG7 5GZ", null,
            [new(DayOfWeek.Wednesday, 19, ClassLevel.Beginners)]),
        new("kettering", "Kettering", "Kettering Science Academy", "Deeble Rd", "NN15 7AA", null,
            [new(DayOfWeek.Wednesday, 19, ClassLevel.Beginners)]),
        new("coventry", "Coventry", "Meadow Park School", "Abbey Rd", "CV3 4BD", null,
            [new(DayOfWeek.Thursday, 19, ClassLevel.Beginners), new(DayOfWeek.Thursday, 20, ClassLevel.Intermediate)]),
        new("wolverhampton", "Wolverhampton", "Dormston School", "Mill Bank, Sedgley", "DY3 1SN", null,
            [new(DayOfWeek.Thursday, 19, ClassLevel.Beginners)]),
        // Content document says "starting in February"; first Thursday of Feb 2027 assumed — admin can change it.
        new("derby", "Derby", "Derby Moor Spencer Academy", "Moorway Lane, Littleover", "DE23 2FS", new DateOnly(2027, 2, 4),
            [new(DayOfWeek.Thursday, 19, ClassLevel.Beginners)]),
    ];

    /// <summary>Returns false (and does nothing) if any venue already exists.</summary>
    public async Task<bool> SeedIfEmptyAsync(CancellationToken ct = default)
    {
        if ((await venues.GetAllAsync(ct)).Count > 0) return false;

        var order = 0;
        foreach (var v in Data)
        {
            await venues.UpsertAsync(new Venue
            {
                Slug = v.Slug, City = v.City, Name = v.Name, Address = v.Address, Postcode = v.Postcode,
                StartsOn = v.StartsOn, IsActive = true, SortOrder = ++order,
            }, ct);
            foreach (var c in v.Classes)
            {
                await classes.UpsertAsync(new ClassSchedule
                {
                    Id = Ids.New(), VenueSlug = v.Slug, DayOfWeek = c.Day,
                    StartTime = new TimeOnly(c.StartHour, 0), EndTime = new TimeOnly(c.StartHour + 1, 0), Level = c.Level,
                }, ct);
            }
        }
        return true;
    }
}
