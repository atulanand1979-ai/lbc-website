using Lbc.Core.Domain;
using Lbc.Core.Payments;
using Lbc.Core.Services;

namespace Lbc.Tests;

public class FlowTests
{
    private static string TokenFrom(string html)
    {
        var i = html.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var end = html.IndexOf('"', i);
        return Uri.UnescapeDataString(html[i..end]);
    }

    [Fact]
    public async Task Pay_as_you_go_booking_is_confirmed_straight_away_with_pay_at_class_email()
    {
        var app = new TestApp();
        var (_, cls) = await app.AddLeicesterTuesdayAsync();

        var booking = await app.Checkout.BookPayAsYouGoAsync(new("leicester", cls.Id, "2026-12-08", "Priya Kaur", "Priya@Example.com", "07700 900123"));

        Assert.Empty(app.Gateway.Requests); // no online payment
        Assert.Equal(BookingStatus.PayAtClass, booking.Status);
        Assert.Equal(750, booking.AmountPence);
        Assert.Equal("priya@example.com", booking.Email);

        var mail = app.Email.Sent.Single();
        Assert.Equal("priya@example.com", mail.To);
        Assert.Contains("pay", mail.HtmlBody);
        Assert.Contains("at the class", mail.HtmlBody);
        Assert.Contains("Create my account", mail.HtmlBody);

        // Following the link sets the password and the booking shows on the account page.
        var user = await app.Accounts.SetPasswordAsync(TokenFrom(mail.HtmlBody), "a-long-password");
        Assert.True(user.HasPassword);
        Assert.NotNull(await app.Accounts.LoginAsync("priya@example.com", "a-long-password"));
        var account = await app.Admin.GetAccountAsync("priya@example.com");
        Assert.Equal("PayAtClass", account.Upcoming.Single().Status);

        // A second booking: no account link once a password exists.
        await app.Checkout.BookPayAsYouGoAsync(new("leicester", cls.Id, "2026-12-15", "Priya Kaur", "priya@example.com", "07700 900123"));
        Assert.DoesNotContain("Create my account", app.Email.Sent[^1].HtmlBody);
    }

    [Fact]
    public async Task Same_person_cannot_book_the_same_class_twice()
    {
        var app = new TestApp();
        var (_, cls) = await app.AddLeicesterTuesdayAsync();
        await app.Checkout.BookPayAsYouGoAsync(new("leicester", cls.Id, "2026-12-08", "A", "a@example.com", "07700900123"));
        await Assert.ThrowsAsync<ValidationException>(() =>
            app.Checkout.BookPayAsYouGoAsync(new("leicester", cls.Id, "2026-12-08", "A", "A@example.com", "07700900123")));
        Assert.Single(app.Bookings.Items);
    }

    [Fact]
    public async Task Duplicate_webhook_is_ignored()
    {
        var app = new TestApp();
        await app.AddLeicesterTuesdayAsync();
        await app.Checkout.StartMembershipAsync(new("silver", null, "A", "a@example.com", "07700900123"));
        var reference = ((RecurringPaymentRequest)app.Gateway.Requests[0]).Reference;

        var e = new PaymentEvent("evt_dup", PaymentEventType.RecurringStarted, reference, "sub_1");
        await app.Processor.HandleAsync(e);
        await app.Processor.HandleAsync(e);
        Assert.Single(app.Email.Sent);
    }

    [Fact]
    public async Task Cannot_book_a_date_the_class_does_not_run()
    {
        var app = new TestApp();
        var (_, cls) = await app.AddLeicesterTuesdayAsync();
        await Assert.ThrowsAsync<ValidationException>(() =>
            app.Checkout.BookPayAsYouGoAsync(new("leicester", cls.Id, "2026-12-09", "A", "a@example.com", "07700900123"))); // a Wednesday
        await app.Schedule.UpdateClassAsync("leicester", cls.Id, new ClassInput("Tuesday", "19:00", "20:00", "Beginners", true, ["2026-12-08"]));
        await Assert.ThrowsAsync<ValidationException>(() =>
            app.Checkout.BookPayAsYouGoAsync(new("leicester", cls.Id, "2026-12-08", "A", "a@example.com", "07700900123"))); // skipped
    }

    [Fact]
    public async Task Bronze_requires_home_venue_and_activates_on_webhook()
    {
        var app = new TestApp();
        await app.AddLeicesterTuesdayAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            app.Checkout.StartMembershipAsync(new("bronze", null, "Raj", "raj@example.com", "07700900123")));

        await app.Checkout.StartMembershipAsync(new("bronze", "leicester", "Raj", "raj@example.com", "07700900123"));
        var req = Assert.IsType<RecurringPaymentRequest>(app.Gateway.Requests.Single());
        Assert.Equal(2500, req.MonthlyAmountPence);

        await app.Processor.HandleAsync(new PaymentEvent("evt_m1", PaymentEventType.RecurringStarted, req.Reference, "sub_1", PeriodEnd: app.Clock.Now.AddMonths(1)));
        var m = app.Memberships.Items.Values.Single();
        Assert.Equal(MembershipStatus.Active, m.Status);
        Assert.Equal("sub_1", m.GatewayRecurringRef);
        Assert.Contains("Leicester", app.Email.Sent.Single().HtmlBody);

        // A second membership is refused while one is live.
        await Assert.ThrowsAsync<ValidationException>(() =>
            app.Checkout.StartMembershipAsync(new("gold", null, "Raj", "raj@example.com", "07700900123")));

        // Failed renewal -> past due; admin cancel stops the gateway subscription.
        await app.Processor.HandleAsync(new PaymentEvent("evt_m2", PaymentEventType.RecurringFailed, req.Reference, "sub_1"));
        Assert.Equal(MembershipStatus.PastDue, m.Status);
        await app.Admin.CancelMembershipAsync(m.Id);
        Assert.Equal(MembershipStatus.Cancelled, m.Status);
        Assert.Equal(["sub_1"], app.Gateway.Cancelled);
    }

    [Fact]
    public async Task Register_lists_confirmed_bookings_and_eligible_members()
    {
        var app = new TestApp();
        var (_, cls) = await app.AddLeicesterTuesdayAsync();
        await app.Schedule.CreateVenueAsync(new VenueInput("coventry", "Coventry", "Meadow Park School", "", "CV3 4BD", null, true, null, 2));

        await app.Checkout.BookPayAsYouGoAsync(new("leicester", cls.Id, "2026-12-08", "Booked Person", "booked@example.com", "07700900123"));
        // A left-over booking from the old pay-online flow that was never paid is not on the register.
        await app.Bookings.UpsertAsync(new Booking
        {
            Id = "old", VenueSlug = "leicester", ClassId = cls.Id, Date = new DateOnly(2026, 12, 8),
            Email = "unpaid@example.com", Name = "Unpaid Person", Status = BookingStatus.Pending,
        });

        async Task Member(string tier, string? venue, string email)
        {
            await app.Checkout.StartMembershipAsync(new(tier, venue, email, email, "07700900123"));
            var r = (RecurringPaymentRequest)app.Gateway.Requests[^1];
            await app.Processor.HandleAsync(new PaymentEvent("m_" + email, PaymentEventType.RecurringStarted, r.Reference, "s_" + email));
        }
        await Member("bronze", "leicester", "bronze-lei@example.com");
        await Member("bronze", "coventry", "bronze-cov@example.com");
        await Member("silver", null, "silver@example.com");

        var register = await app.Admin.GetRegisterAsync("leicester", new DateOnly(2026, 12, 8));
        Assert.Equal("Booked Person", register.Classes.Single().PayAsYouGo.Single().Name);
        Assert.Equal(["bronze-lei@example.com", "silver@example.com"], register.Members.Select(m => m.Email).Order().ToArray());
    }

    [Fact]
    public async Task Set_password_link_is_single_use_and_expires()
    {
        var app = new TestApp();
        await app.Accounts.EnsureUserAsync("x@example.com", "X", "");
        var url = await app.Accounts.CreateLinkAsync("x@example.com", AccountTokenPurpose.SetPassword);
        var token = Uri.UnescapeDataString(url[(url.IndexOf("token=") + 6)..]);

        await app.Accounts.SetPasswordAsync(token, "first-password-1");
        await Assert.ThrowsAsync<ValidationException>(() => app.Accounts.SetPasswordAsync(token, "second-password-2"));

        var url2 = await app.Accounts.CreateLinkAsync("x@example.com", AccountTokenPurpose.ResetPassword);
        app.Clock.Now = app.Clock.Now.AddHours(2);
        await Assert.ThrowsAsync<ValidationException>(() =>
            app.Accounts.SetPasswordAsync(Uri.UnescapeDataString(url2[(url2.IndexOf("token=") + 6)..]), "third-password-3"));
    }

    [Fact]
    public void Payment_references_round_trip()
    {
        var r = PaymentReference.ForBooking("abc123", new DateOnly(2026, 12, 8), "bk1");
        Assert.True(PaymentReference.TryParseBooking(r, out var c, out var d, out var b));
        Assert.Equal(("abc123", new DateOnly(2026, 12, 8), "bk1"), (c, d, b));
        Assert.True(PaymentReference.TryParseMembership(PaymentReference.ForMembership("m1"), out var m));
        Assert.Equal("m1", m);
        Assert.False(PaymentReference.TryParseMembership(r, out _));
    }
}
