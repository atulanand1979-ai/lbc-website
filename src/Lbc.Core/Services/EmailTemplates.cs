using System.Net;
using Lbc.Core.Abstractions;
using Lbc.Core.Domain;

namespace Lbc.Core.Services;

/// <summary>Plain, inline-styled HTML emails that render in every client.</summary>
public static class EmailTemplates
{
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string Layout(string businessName, string heading, string bodyHtml) => $"""
        <!doctype html><html><body style="margin:0;background:#F5F3EC;font-family:Arial,Helvetica,sans-serif;color:#0B0B0B">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:24px 12px">
        <table role="presentation" width="100%" style="max-width:560px;background:#FFFFFF;border:2px solid #0B0B0B;border-radius:12px" cellpadding="0" cellspacing="0">
        <tr><td style="background:#0B0B0B;color:#FFD400;padding:18px 24px;font-size:20px;font-weight:bold;border-radius:10px 10px 0 0">{E(businessName)}</td></tr>
        <tr><td style="padding:24px">
        <h1 style="font-size:22px;margin:0 0 16px">{E(heading)}</h1>
        {bodyHtml}
        </td></tr></table></td></tr></table></body></html>
        """;

    private static string Button(string url, string label) =>
        $"""<p style="margin:24px 0"><a href="{E(url)}" style="background:#FFD400;color:#0B0B0B;border:2px solid #0B0B0B;border-radius:999px;padding:12px 24px;font-weight:bold;text-decoration:none;display:inline-block">{E(label)}</a></p>""";

    public static EmailMessage BookingConfirmed(SiteOptions site, Booking booking, Venue? venue, string? setPasswordUrl)
    {
        var where = venue is null ? "" : $"{venue.Name}, {venue.Address}, {venue.Postcode}";
        var account = setPasswordUrl is null
            ? Button($"{site.BaseUrl}/account/", "View my bookings")
            : $"<p>Create your account to see your bookings any time:</p>{Button(setPasswordUrl, "Create my account")}<p style=\"font-size:13px;color:#454545\">This link expires in 7 days.</p>";
        var body = $"""
            <p>Hi {E(booking.Name)}, thanks for booking â€” you're in!</p>
            <table role="presentation" cellpadding="6" style="border-collapse:collapse;font-size:15px">
            <tr><td><b>Class</b></td><td>{E(booking.ClassLabel)}</td></tr>
            <tr><td><b>Date</b></td><td>{booking.Date:dddd d MMMM yyyy}</td></tr>
            <tr><td><b>Where</b></td><td>{E(where)}</td></tr>
            <tr><td><b>Paid</b></td><td>{Money.Format(booking.AmountPence)}</td></tr>
            </table>
            <p>Just give your name to the teacher when you arrive. Wear comfy clothes and trainers and bring water.</p>
            {account}
            """;
        return new(booking.Email, $"Booking confirmed: {booking.Date:ddd d MMM}", Layout(site.BusinessName, "Your class is booked", body));
    }

    public static EmailMessage MembershipStarted(SiteOptions site, User user, Membership membership, Venue? homeVenue, string? setPasswordUrl)
    {
        var scope = membership.Tier switch
        {
            MembershipTier.Bronze => $"Unlimited Beginners classes in {E(homeVenue?.City ?? membership.HomeVenueSlug)}.",
            MembershipTier.Silver => "Unlimited Beginners classes at all our locations.",
            _ => "Unlimited Beginners and advanced classes at all our locations, plus Gold perks.",
        };
        var account = setPasswordUrl is null
            ? Button($"{site.BaseUrl}/account/", "View my membership")
            : $"<p>Create your account to view your membership any time:</p>{Button(setPasswordUrl, "Create my account")}<p style=\"font-size:13px;color:#454545\">This link expires in 7 days.</p>";
        var body = $"""
            <p>Hi {E(user.Name)}, welcome to the LBC family!</p>
            <p><b>{membership.Tier} membership</b> â€” {Money.Format(membership.MonthlyPricePence)} per month.<br>{scope}</p>
            <p>No need to book â€” just turn up and give your name to the teacher.</p>
            {account}
            """;
        return new(user.Email, $"Welcome to your {membership.Tier} membership", Layout(site.BusinessName, "Membership active", body));
    }

    public static EmailMessage MembershipPaymentFailed(SiteOptions site, User user, Membership membership)
    {
        var body = $"""
            <p>Hi {E(user.Name)}, we couldn't take this month's payment for your {membership.Tier} membership.</p>
            <p>Please check your payment details or get in touch with us at <a href="mailto:{E(site.AdminEmail)}">{E(site.AdminEmail)}</a>.</p>
            """;
        return new(user.Email, "Your membership payment didn't go through", Layout(site.BusinessName, "Payment problem", body));
    }

    public static EmailMessage SetPassword(SiteOptions site, string email, string url, bool isReset)
    {
        var body = isReset
            ? $"<p>Someone asked to reset the password for this account. If that was you, use the button below.</p>{Button(url, "Reset my password")}<p style=\"font-size:13px;color:#454545\">This link expires in 1 hour. If you didn't ask for this, ignore this email.</p>"
            : $"<p>Use the button below to set your password and access your account.</p>{Button(url, "Set my password")}<p style=\"font-size:13px;color:#454545\">This link expires in 7 days.</p>";
        return new(email, isReset ? "Reset your password" : "Set up your account", Layout(site.BusinessName, isReset ? "Reset your password" : "Set up your account", body));
    }

    public static EmailMessage EnquiryNotification(SiteOptions site, Enquiry e)
    {
        var body = $"""
            <table role="presentation" cellpadding="6" style="border-collapse:collapse;font-size:15px">
            <tr><td><b>Type</b></td><td>{e.Type}</td></tr>
            <tr><td><b>Topic</b></td><td>{E(e.Topic)}</td></tr>
            <tr><td><b>Name</b></td><td>{E(e.Name)}</td></tr>
            <tr><td><b>Email</b></td><td>{E(e.Email)}</td></tr>
            <tr><td><b>Phone</b></td><td>{E(e.Phone)}</td></tr>
            <tr><td><b>Event date</b></td><td>{e.EventDate?.ToString("d MMM yyyy") ?? "â€”"}</td></tr>
            </table>
            <p style="white-space:pre-wrap">{E(e.Message)}</p>
            """;
        return new(site.AdminEmail, $"New {e.Type} enquiry from {e.Name}", Layout(site.BusinessName, "New enquiry", body), ReplyTo: e.Email);
    }
}
