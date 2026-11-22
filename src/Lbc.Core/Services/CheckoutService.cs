using Lbc.Core.Abstractions;
using Lbc.Core.Domain;
using Lbc.Core.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lbc.Core.Services;

public sealed record PayAsYouGoRequest(string? VenueSlug, string? ClassId, string? Date, string? Name, string? Email, string? Phone);

public sealed record MembershipRequest(string? Tier, string? HomeVenueSlug, string? Name, string? Email, string? Phone);

/// <summary>
/// Pay As You Go: confirms the booking straight away (customer pays at the class).
/// Memberships: creates a pending membership and hands the customer to the gateway's hosted checkout.
/// </summary>
public sealed class CheckoutService(
    IVenueRepository venues,
    IClassRepository classes,
    IBookingRepository bookings,
    IMembershipRepository memberships,
    AccountService accounts,
    IPaymentGateway gateway,
    IEmailSender email,
    IOptions<PricingOptions> pricing,
    IOptions<SiteOptions> site,
    TimeProvider clock,
    ILogger<CheckoutService> logger)
{
    public async Task<Booking> BookPayAsYouGoAsync(PayAsYouGoRequest r, CancellationToken ct = default)
    {
        var name = Guard.Required(r.Name, "Name", 100);
        var emailAddress = Guard.Email(r.Email);
        var phone = Guard.Phone(r.Phone, required: true);
        if (!DateOnly.TryParseExact(r.Date ?? "", "yyyy-MM-dd", out var date))
            throw new ValidationException("Please choose a class date.");

        var venue = await venues.GetAsync(r.VenueSlug ?? "", ct) ?? throw new ValidationException("Please choose a location.");
        var schedule = await classes.GetAsync(venue.Slug, r.ClassId ?? "", ct) ?? throw new ValidationException("Please choose a class.");
        if (!ScheduleCalculator.IsBookable(venue, schedule, date, UkTime.Now(clock), site.Value.BookableWeeksAhead))
            throw new ValidationException("That class date is no longer available. Please choose another date.");

        var alreadyBooked = (await bookings.GetForSessionAsync(schedule.Id, date, ct))
            .Any(b => b.Email == emailAddress && b.Status != BookingStatus.Pending);
        if (alreadyBooked)
            throw new ValidationException("You're already booked into this class on that date — see you there!");

        var user = await accounts.EnsureUserAsync(emailAddress, name, phone, ct);

        var booking = new Booking
        {
            Id = Ids.New(),
            VenueSlug = venue.Slug,
            ClassId = schedule.Id,
            Date = date,
            Email = emailAddress,
            Name = name,
            Phone = phone,
            Status = BookingStatus.PayAtClass,
            AmountPence = pricing.Value.PayAsYouGoPence,
            ClassLabel = $"{venue.City} – {ScheduleCalculator.Describe(schedule)}",
            CreatedAt = clock.GetUtcNow(),
        };
        await bookings.UpsertAsync(booking, ct);

        // The booking stands even if the email fails; the customer still sees the success page.
        try
        {
            var link = user.HasPassword ? null : await accounts.CreateLinkAsync(emailAddress, AccountTokenPurpose.SetPassword, ct);
            await email.SendAsync(EmailTemplates.BookingConfirmed(site.Value, booking, venue, link), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send booking confirmation for {BookingId}", booking.Id);
        }
        return booking;
    }

    public async Task<CheckoutSession> StartMembershipAsync(MembershipRequest r, CancellationToken ct = default)
    {
        var name = Guard.Required(r.Name, "Name", 100);
        var emailAddress = Guard.Email(r.Email);
        var phone = Guard.Phone(r.Phone, required: true);
        if (!Enum.TryParse<MembershipTier>(r.Tier, ignoreCase: true, out var tier) || !Enum.IsDefined(tier))
            throw new ValidationException("Please choose a membership.");

        string? homeVenue = null;
        if (tier == MembershipTier.Bronze)
        {
            var venue = await venues.GetAsync(r.HomeVenueSlug ?? "", ct);
            if (venue is not { IsActive: true }) throw new ValidationException("Please choose your home location for Bronze membership.");
            homeVenue = venue.Slug;
        }

        var existing = await memberships.GetByEmailAsync(emailAddress, ct);
        if (existing.Any(m => m.IsLive))
            throw new ValidationException("You already have an active membership. Please contact us if you'd like to change it.");

        await accounts.EnsureUserAsync(emailAddress, name, phone, ct);

        var membership = new Membership
        {
            Id = Ids.New(),
            Email = emailAddress,
            Tier = tier,
            HomeVenueSlug = homeVenue,
            MonthlyPricePence = pricing.Value.ForTier(tier),
            CreatedAt = clock.GetUtcNow(),
        };
        await memberships.UpsertAsync(membership, ct);

        var baseUrl = site.Value.BaseUrl.TrimEnd('/');
        return await gateway.CreateRecurringCheckoutAsync(new RecurringPaymentRequest(
            PaymentReference.ForMembership(membership.Id),
            membership.MonthlyPricePence,
            $"{tier} membership – {site.Value.BusinessName}",
            emailAddress, name,
            SuccessUrl: $"{baseUrl}/join/success.html",
            CancelUrl: $"{baseUrl}/signup.html?cancelled=1"), ct);
    }
}
