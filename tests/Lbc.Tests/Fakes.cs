using Lbc.Core;
using Lbc.Core.Abstractions;
using Lbc.Core.Domain;
using Lbc.Core.Payments;
using Lbc.Core.Services;
using Lbc.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lbc.Tests;

public sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class MemVenues : IVenueRepository
{
    public readonly Dictionary<string, Venue> Items = [];
    public Task<IReadOnlyList<Venue>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Venue>>(Items.Values.ToList());
    public Task<Venue?> GetAsync(string slug, CancellationToken ct = default) => Task.FromResult(Items.GetValueOrDefault(slug));
    public Task UpsertAsync(Venue v, CancellationToken ct = default) { Items[v.Slug] = v; return Task.CompletedTask; }
    public Task DeleteAsync(string slug, CancellationToken ct = default) { Items.Remove(slug); return Task.CompletedTask; }
}

public sealed class MemClasses : IClassRepository
{
    public readonly List<ClassSchedule> Items = [];
    public Task<IReadOnlyList<ClassSchedule>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ClassSchedule>>(Items.ToList());
    public Task<IReadOnlyList<ClassSchedule>> GetByVenueAsync(string venueSlug, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ClassSchedule>>(Items.Where(c => c.VenueSlug == venueSlug).ToList());
    public Task<ClassSchedule?> GetAsync(string venueSlug, string classId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(c => c.VenueSlug == venueSlug && c.Id == classId));
    public Task UpsertAsync(ClassSchedule s, CancellationToken ct = default) { Items.RemoveAll(c => c.Id == s.Id); Items.Add(s); return Task.CompletedTask; }
    public Task DeleteAsync(string venueSlug, string classId, CancellationToken ct = default) { Items.RemoveAll(c => c.Id == classId); return Task.CompletedTask; }
}

public sealed class MemUsers : IUserRepository
{
    public readonly Dictionary<string, User> Items = [];
    public Task<User?> GetAsync(string email, CancellationToken ct = default) => Task.FromResult(Items.GetValueOrDefault(email));
    public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<User>>(Items.Values.ToList());
    public Task<bool> TryAddAsync(User u, CancellationToken ct = default) => Task.FromResult(Items.TryAdd(u.Email, u));
    public Task UpdateAsync(User u, CancellationToken ct = default) { Items[u.Email] = u; return Task.CompletedTask; }
}

public sealed class MemMemberships : IMembershipRepository
{
    public readonly Dictionary<string, Membership> Items = [];
    public Task<Membership?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(Items.GetValueOrDefault(id));
    public Task<IReadOnlyList<Membership>> GetByEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Membership>>(Items.Values.Where(m => m.Email == email).ToList());
    public Task<IReadOnlyList<Membership>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Membership>>(Items.Values.ToList());
    public Task UpsertAsync(Membership m, CancellationToken ct = default) { Items[m.Id] = m; return Task.CompletedTask; }
}

public sealed class MemBookings : IBookingRepository
{
    public readonly Dictionary<string, Booking> Items = [];
    public Task UpsertAsync(Booking b, CancellationToken ct = default) { Items[b.Id] = b; return Task.CompletedTask; }
    public Task<Booking?> GetAsync(string classId, DateOnly date, string bookingId, CancellationToken ct = default) =>
        Task.FromResult(Items.GetValueOrDefault(bookingId) is { } b && b.ClassId == classId && b.Date == date ? b : null);
    public Task<IReadOnlyList<Booking>> GetForSessionAsync(string classId, DateOnly date, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Booking>>(Items.Values.Where(b => b.ClassId == classId && b.Date == date).ToList());
    public Task<IReadOnlyList<Booking>> GetByEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Booking>>(Items.Values.Where(b => b.Email == email).ToList());
}

public sealed class MemEnquiries : IEnquiryRepository
{
    public readonly List<Enquiry> Items = [];
    public Task AddAsync(Enquiry e, CancellationToken ct = default) { Items.Add(e); return Task.CompletedTask; }
    public Task<IReadOnlyList<Enquiry>> GetSinceAsync(DateTimeOffset since, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Enquiry>>(Items.Where(e => e.CreatedAt >= since).ToList());
}

public sealed class MemTokens : IAccountTokenRepository
{
    public readonly Dictionary<string, AccountToken> Items = [];
    public Task AddAsync(AccountToken t, CancellationToken ct = default) { Items.Add(t.TokenHash, t); return Task.CompletedTask; }
    public Task<AccountToken?> GetAsync(string hash, CancellationToken ct = default) => Task.FromResult(Items.GetValueOrDefault(hash));
    public Task UpdateAsync(AccountToken t, CancellationToken ct = default) { Items[t.TokenHash] = t; return Task.CompletedTask; }
}

public sealed class MemEventLog : IPaymentEventLog
{
    public readonly HashSet<string> Claimed = [];
    public Task<bool> TryClaimAsync(string eventId, string type, CancellationToken ct = default) => Task.FromResult(Claimed.Add(eventId));
    public Task ReleaseAsync(string eventId, CancellationToken ct = default) { Claimed.Remove(eventId); return Task.CompletedTask; }
}

public sealed class MemEmail : IEmailSender
{
    public readonly List<EmailMessage> Sent = [];
    public Task SendAsync(EmailMessage m, CancellationToken ct = default) { Sent.Add(m); return Task.CompletedTask; }
}

public sealed class RecordingGateway : IPaymentGateway
{
    public readonly List<object> Requests = [];
    public readonly List<string> Cancelled = [];
    public Task<CheckoutSession> CreateOneOffCheckoutAsync(OneOffPaymentRequest r, CancellationToken ct = default) { Requests.Add(r); return Task.FromResult(new CheckoutSession("https://pay.example/one-off", null)); }
    public Task<CheckoutSession> CreateRecurringCheckoutAsync(RecurringPaymentRequest r, CancellationToken ct = default) { Requests.Add(r); return Task.FromResult(new CheckoutSession("https://pay.example/recurring", null)); }
    public Task CancelRecurringAsync(string gatewayRecurringRef, CancellationToken ct = default) { Cancelled.Add(gatewayRecurringRef); return Task.CompletedTask; }
    public Task<PaymentEvent?> ParseWebhookAsync(WebhookRequest request, CancellationToken ct = default) => Task.FromResult<PaymentEvent?>(null);
}

/// <summary>Wires the real services over in-memory stores.</summary>
public sealed class TestApp
{
    // Tuesday 1 Dec 2026, 10:00 UK (GMT in winter).
    public readonly FakeClock Clock = new(new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero));
    public readonly MemVenues Venues = new();
    public readonly MemClasses Classes = new();
    public readonly MemUsers Users = new();
    public readonly MemMemberships Memberships = new();
    public readonly MemBookings Bookings = new();
    public readonly MemEnquiries Enquiries = new();
    public readonly MemTokens Tokens = new();
    public readonly MemEventLog EventLog = new();
    public readonly MemEmail Email = new();
    public readonly RecordingGateway Gateway = new();
    public readonly IOptions<SiteOptions> Site = Options.Create(new SiteOptions { BaseUrl = "https://lbc.test" });
    public readonly IOptions<PricingOptions> Pricing = Options.Create(new PricingOptions());

    public readonly AccountService Accounts;
    public readonly CheckoutService Checkout;
    public readonly PaymentEventProcessor Processor;
    public readonly MemberAdminService Admin;
    public readonly ScheduleAdminService Schedule;
    public readonly CatalogService Catalog;

    public TestApp()
    {
        Accounts = new AccountService(Users, Tokens, new PasswordService(), Email, Site, Clock);
        Checkout = new CheckoutService(Venues, Classes, Bookings, Memberships, Accounts, Gateway, Email, Pricing, Site, Clock, NullLogger<CheckoutService>.Instance);
        Processor = new PaymentEventProcessor(EventLog, Bookings, Memberships, Users, Venues, Accounts, Email, Site, Clock, NullLogger<PaymentEventProcessor>.Instance);
        Admin = new MemberAdminService(Venues, Classes, Bookings, Memberships, Users, Enquiries, Gateway, Accounts, Email, Site, Clock);
        Schedule = new ScheduleAdminService(Venues, Classes);
        Catalog = new CatalogService(Venues, Classes, Pricing, Site, Clock);
    }

    public async Task<(Venue venue, ClassSchedule cls)> AddLeicesterTuesdayAsync()
    {
        var venue = await Schedule.CreateVenueAsync(new VenueInput("leicester", "Leicester", "Avanti Fields School", "21 Furrow View", "LE5 0BX", null, true, null, 1));
        var cls = await Schedule.CreateClassAsync("leicester", new ClassInput("Tuesday", "19:00", "20:00", "Beginners", true, []));
        return (venue, cls);
    }
}
