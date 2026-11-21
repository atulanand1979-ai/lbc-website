using Lbc.Core.Abstractions;
using Lbc.Core.Domain;
using Lbc.Core.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lbc.Core.Services;

/// <summary>Applies normalised gateway events to our own records. Safe to call repeatedly with the same event.</summary>
public sealed class PaymentEventProcessor(
    IPaymentEventLog eventLog,
    IBookingRepository bookings,
    IMembershipRepository memberships,
    IUserRepository users,
    IVenueRepository venues,
    AccountService accounts,
    IEmailSender email,
    IOptions<SiteOptions> site,
    TimeProvider clock,
    ILogger<PaymentEventProcessor> logger)
{
    public async Task HandleAsync(PaymentEvent e, CancellationToken ct = default)
    {
        if (!await eventLog.TryClaimAsync(e.EventId, e.Type.ToString(), ct))
        {
            logger.LogInformation("Payment event {EventId} already processed", e.EventId);
            return;
        }

        try
        {
            switch (e.Type)
            {
                case PaymentEventType.OneOffPaid: await BookingPaidAsync(e, ct); break;
                default: await MembershipEventAsync(e, ct); break;
            }
        }
        catch
        {
            await eventLog.ReleaseAsync(e.EventId, ct);
            throw;
        }
    }

    private async Task BookingPaidAsync(PaymentEvent e, CancellationToken ct)
    {
        if (!PaymentReference.TryParseBooking(e.Reference, out var classId, out var date, out var bookingId))
        {
            logger.LogWarning("Payment event {EventId} has unrecognised booking reference {Reference}", e.EventId, e.Reference);
            return;
        }

        var booking = await bookings.GetAsync(classId, date, bookingId, ct);
        if (booking is null)
        {
            logger.LogError("Paid booking {Reference} not found", e.Reference);
            return;
        }
        if (booking.Status == BookingStatus.Paid) return;

        if (e.AmountPence is { } paid && paid != booking.AmountPence)
            logger.LogWarning("Booking {BookingId} paid {Paid}p but expected {Expected}p", booking.Id, paid, booking.AmountPence);

        booking.Status = BookingStatus.Paid;
        booking.PaidAt = clock.GetUtcNow();
        booking.GatewayPaymentRef = e.GatewayRef;
        await bookings.UpsertAsync(booking, ct);

        var user = await users.GetAsync(booking.Email, ct);
        var link = user is { HasPassword: false } ? await accounts.CreateLinkAsync(booking.Email, AccountTokenPurpose.SetPassword, ct) : null;
        var venue = await venues.GetAsync(booking.VenueSlug, ct);
        await SendSafelyAsync(EmailTemplates.BookingConfirmed(site.Value, booking, venue, link), ct);
    }

    private async Task MembershipEventAsync(PaymentEvent e, CancellationToken ct)
    {
        if (!PaymentReference.TryParseMembership(e.Reference, out var membershipId))
        {
            logger.LogWarning("Payment event {EventId} has unrecognised membership reference {Reference}", e.EventId, e.Reference);
            return;
        }

        var m = await memberships.GetAsync(membershipId, ct);
        if (m is null)
        {
            logger.LogError("Membership {Reference} not found", e.Reference);
            return;
        }

        var wasPending = m.Status == MembershipStatus.Pending;
        if (e.GatewayRef is not null) m.GatewayRecurringRef = e.GatewayRef;
        if (e.PeriodEnd is not null) m.CurrentPeriodEnd = e.PeriodEnd;

        switch (e.Type)
        {
            case PaymentEventType.RecurringStarted:
            case PaymentEventType.RecurringRenewed:
                if (m.Status == MembershipStatus.Cancelled) break; // late event after admin cancelled
                m.Status = MembershipStatus.Active;
                break;
            case PaymentEventType.RecurringFailed:
                if (m.Status == MembershipStatus.Active) m.Status = MembershipStatus.PastDue;
                break;
            case PaymentEventType.RecurringCancelled:
                m.Status = MembershipStatus.Cancelled;
                m.CancelledAt ??= clock.GetUtcNow();
                break;
        }
        await memberships.UpsertAsync(m, ct);

        var user = await users.GetAsync(m.Email, ct);
        if (user is null) return;

        if (wasPending && m.Status == MembershipStatus.Active)
        {
            var link = user.HasPassword ? null : await accounts.CreateLinkAsync(user.Email, AccountTokenPurpose.SetPassword, ct);
            var home = m.HomeVenueSlug is null ? null : await venues.GetAsync(m.HomeVenueSlug, ct);
            await SendSafelyAsync(EmailTemplates.MembershipStarted(site.Value, user, m, home, link), ct);
        }
        else if (e.Type == PaymentEventType.RecurringFailed)
        {
            await SendSafelyAsync(EmailTemplates.MembershipPaymentFailed(site.Value, user, m), ct);
        }
    }

    /// <summary>The payment is already recorded; an email failure must not make the gateway retry the webhook.</summary>
    private async Task SendSafelyAsync(EmailMessage message, CancellationToken ct)
    {
        try { await email.SendAsync(message, ct); }
        catch (Exception ex) { logger.LogError(ex, "Failed to send email '{Subject}' to {To}", message.Subject, message.To); }
    }
}
