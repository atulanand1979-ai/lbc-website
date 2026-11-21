using Lbc.Core.Abstractions;
using Lbc.Core.Domain;
using Lbc.Core.Payments;
using Microsoft.Extensions.Options;

namespace Lbc.Core.Services;

public sealed record BookingDto(string Id, string Date, string ClassLabel, string VenueSlug, string Name, string Email, string Phone, string Status, int AmountPence);

public sealed record MembershipDto(
    string Id, string Email, string Name, string Phone, string Tier, string? HomeVenueSlug, string? HomeVenueCity,
    string Status, int MonthlyPricePence, string? CurrentPeriodEnd, string CreatedAt);

public sealed record RegisterClassDto(ClassDto Class, IReadOnlyList<BookingDto> PayAsYouGo);

public sealed record RegisterDto(string VenueSlug, string VenueCity, string Date, IReadOnlyList<RegisterClassDto> Classes, IReadOnlyList<MembershipDto> Members);

public sealed record AccountDto(string Name, string Email, string Phone, string Role, MembershipDto? Membership, IReadOnlyList<BookingDto> Upcoming, IReadOnlyList<BookingDto> Past);

public sealed record EnquiryDto(string Id, string Type, string Name, string Email, string Phone, string Topic, string? EventDate, string Message, string CreatedAt);

/// <summary>Member account view and admin operations on members, registers and enquiries.</summary>
public sealed class MemberAdminService(
    IVenueRepository venues,
    IClassRepository classes,
    IBookingRepository bookings,
    IMembershipRepository memberships,
    IUserRepository users,
    IEnquiryRepository enquiries,
    IPaymentGateway gateway,
    AccountService accounts,
    IEmailSender email,
    IOptions<SiteOptions> site,
    TimeProvider clock)
{
    public async Task<AccountDto> GetAccountAsync(string userEmail, CancellationToken ct = default)
    {
        var user = await users.GetAsync(userEmail, ct) ?? throw new NotFoundException("Account not found.");
        var allVenues = (await venues.GetAllAsync(ct)).ToDictionary(v => v.Slug);
        var membership = (await memberships.GetByEmailAsync(user.Email, ct))
            .Where(m => m.Status != MembershipStatus.Pending)
            .OrderByDescending(m => m.IsLive).ThenByDescending(m => m.CreatedAt)
            .FirstOrDefault();

        var today = UkTime.Today(clock);
        var paid = (await bookings.GetByEmailAsync(user.Email, ct)).Where(b => b.Status == BookingStatus.Paid).ToList();
        return new AccountDto(
            user.Name, user.Email, user.Phone, user.Role.ToString(),
            membership is null ? null : ToDto(membership, user, allVenues),
            paid.Where(b => b.Date >= today).OrderBy(b => b.Date).Select(ToDto).ToList(),
            paid.Where(b => b.Date < today).OrderByDescending(b => b.Date).Take(50).Select(ToDto).ToList());
    }

    /// <summary>Class register for a place on a date: paid PAYG bookings per class, plus members allowed at this place.</summary>
    public async Task<RegisterDto> GetRegisterAsync(string venueSlug, DateOnly date, CancellationToken ct = default)
    {
        var venue = await venues.GetAsync(venueSlug, ct) ?? throw new NotFoundException("Location not found.");
        var venueClasses = (await classes.GetByVenueAsync(venueSlug, ct)).Where(c => c.DayOfWeek == date.DayOfWeek).OrderBy(c => c.StartTime);

        var registerClasses = new List<RegisterClassDto>();
        foreach (var c in venueClasses)
        {
            var paid = (await bookings.GetForSessionAsync(c.Id, date, ct))
                .Where(b => b.Status == BookingStatus.Paid).OrderBy(b => b.Name).Select(ToDto).ToList();
            registerClasses.Add(new RegisterClassDto(CatalogService.ToDto(c), paid));
        }

        var allVenues = new Dictionary<string, Venue> { [venue.Slug] = venue };
        var usersByEmail = (await users.GetAllAsync(ct)).ToDictionary(u => u.Email);
        var members = (await memberships.GetAllAsync(ct))
            .Where(m => m.IsLive && (m.Tier != MembershipTier.Bronze || m.HomeVenueSlug == venueSlug))
            .Select(m => ToDto(m, usersByEmail.GetValueOrDefault(m.Email), allVenues))
            .OrderBy(m => m.Tier).ThenBy(m => m.Name)
            .ToList();

        return new RegisterDto(venue.Slug, venue.City, date.ToString("yyyy-MM-dd"), registerClasses, members);
    }

    public async Task<IReadOnlyList<MembershipDto>> GetMembershipsAsync(bool includePending, CancellationToken ct = default)
    {
        var allVenues = (await venues.GetAllAsync(ct)).ToDictionary(v => v.Slug);
        var usersByEmail = (await users.GetAllAsync(ct)).ToDictionary(u => u.Email);
        return (await memberships.GetAllAsync(ct))
            .Where(m => includePending || m.Status != MembershipStatus.Pending)
            .OrderBy(m => m.Status).ThenBy(m => m.Tier).ThenByDescending(m => m.CreatedAt)
            .Select(m => ToDto(m, usersByEmail.GetValueOrDefault(m.Email), allVenues))
            .ToList();
    }

    public async Task<MembershipDto> ChangeHomeVenueAsync(string membershipId, string? venueSlug, CancellationToken ct = default)
    {
        var m = await memberships.GetAsync(membershipId, ct) ?? throw new NotFoundException("Membership not found.");
        if (m.Tier != MembershipTier.Bronze) throw new ValidationException("Only Bronze memberships have a home location.");
        var venue = await venues.GetAsync(venueSlug ?? "", ct) ?? throw new ValidationException("Please choose a location.");
        m.HomeVenueSlug = venue.Slug;
        await memberships.UpsertAsync(m, ct);
        return ToDto(m, await users.GetAsync(m.Email, ct), new Dictionary<string, Venue> { [venue.Slug] = venue });
    }

    /// <summary>Stops future payments at the gateway, then marks the membership cancelled.</summary>
    public async Task<MembershipDto> CancelMembershipAsync(string membershipId, CancellationToken ct = default)
    {
        var m = await memberships.GetAsync(membershipId, ct) ?? throw new NotFoundException("Membership not found.");
        if (m.Status == MembershipStatus.Cancelled) throw new ValidationException("This membership is already cancelled.");
        if (m.GatewayRecurringRef is not null) await gateway.CancelRecurringAsync(m.GatewayRecurringRef, ct);
        m.Status = MembershipStatus.Cancelled;
        m.CancelledAt = clock.GetUtcNow();
        await memberships.UpsertAsync(m, ct);
        return ToDto(m, await users.GetAsync(m.Email, ct), (await venues.GetAllAsync(ct)).ToDictionary(v => v.Slug));
    }

    public async Task<IReadOnlyList<EnquiryDto>> GetEnquiriesAsync(int days, CancellationToken ct = default) =>
        (await enquiries.GetSinceAsync(clock.GetUtcNow().AddDays(-Math.Clamp(days, 1, 366)), ct))
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new EnquiryDto(e.Id, e.Type.ToString(), e.Name, e.Email, e.Phone, e.Topic,
                e.EventDate?.ToString("yyyy-MM-dd"), e.Message, e.CreatedAt.ToString("O")))
            .ToList();

    /// <summary>
    /// One-off setup: creates the first admin (or promotes an existing user) and emails a set-password link.
    /// Refuses once any admin exists.
    /// </summary>
    public async Task BootstrapAdminAsync(string? rawEmail, string? name, CancellationToken ct = default)
    {
        if ((await users.GetAllAsync(ct)).Any(u => u.Role == UserRole.Admin))
            throw new ValidationException("An admin already exists.");
        var adminEmail = Guard.Email(rawEmail);
        var user = await accounts.EnsureUserAsync(adminEmail, Guard.Required(name, "Name", 100), "", ct);
        user.Role = UserRole.Admin;
        await users.UpdateAsync(user, ct);
        var url = await accounts.CreateLinkAsync(user.Email, AccountTokenPurpose.SetPassword, ct);
        await email.SendAsync(EmailTemplates.SetPassword(site.Value, user.Email, url, isReset: false), ct);
    }

    private static BookingDto ToDto(Booking b) => new(
        b.Id, b.Date.ToString("yyyy-MM-dd"), b.ClassLabel, b.VenueSlug, b.Name, b.Email, b.Phone, b.Status.ToString(), b.AmountPence);

    private static MembershipDto ToDto(Membership m, User? u, IReadOnlyDictionary<string, Venue> venueLookup) => new(
        m.Id, m.Email, u?.Name ?? "", u?.Phone ?? "", m.Tier.ToString(), m.HomeVenueSlug,
        m.HomeVenueSlug is not null && venueLookup.TryGetValue(m.HomeVenueSlug, out var v) ? v.City : null,
        m.Status.ToString(), m.MonthlyPricePence, m.CurrentPeriodEnd?.ToString("O"), m.CreatedAt.ToString("O"));
}
