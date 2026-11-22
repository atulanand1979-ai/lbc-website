using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Lbc.Core.Domain;
using Lbc.Core.Payments;
using Lbc.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lbc.Tests;

public class GoCardlessTests
{
    private const string Secret = "whsec-test";

    /// <summary>Stands in for the GoCardless API: canned responses by "METHOD path", records requests.</summary>
    private sealed class StubApi : HttpMessageHandler
    {
        public readonly Dictionary<string, (HttpStatusCode Status, string Body)> Responses = [];
        public readonly List<(string Key, string? Body, string? IdempotencyKey)> Requests = [];

        public StubApi On(string key, object body, HttpStatusCode status = HttpStatusCode.OK)
        {
            Responses[key] = (status, System.Text.Json.JsonSerializer.Serialize(body));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((key, body, request.Headers.TryGetValues("Idempotency-Key", out var v) ? v.First() : null));
            if (!Responses.TryGetValue(key, out var r)) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            return new HttpResponseMessage(r.Status) { Content = new StringContent(r.Body, Encoding.UTF8, "application/json") };
        }
    }

    private static GoCardlessPaymentGateway Gateway(StubApi api) => new(
        new HttpClient(api),
        Options.Create(new PaymentOptions { Provider = "GoCardless", GoCardless = new() { AccessToken = "tok", WebhookSecret = Secret } }),
        NullLogger<GoCardlessPaymentGateway>.Instance);

    private static WebhookRequest Webhook(params object[] events)
    {
        var body = System.Text.Json.JsonSerializer.Serialize(new { events });
        return new WebhookRequest(body, new Dictionary<string, string> { [GoCardlessPaymentGateway.SignatureHeader] = GoCardlessPaymentGateway.Sign(Secret, body) });
    }

    [Fact]
    public async Task Checkout_creates_mandate_billing_request_and_returns_hosted_page()
    {
        var api = new StubApi()
            .On("POST /billing_requests", new { billing_requests = new { id = "BRQ1" } })
            .On("POST /billing_request_flows", new { billing_request_flows = new { authorisation_url = "https://pay-sandbox.gocardless.com/flow/BRF1" } });

        var session = await Gateway(api).CreateRecurringCheckoutAsync(new RecurringPaymentRequest(
            "MS|m1", 2500, "Bronze membership", "a@example.com", "Aman Singh Gill", "https://site/ok", "https://site/no"));

        Assert.Equal("https://pay-sandbox.gocardless.com/flow/BRF1", session.RedirectUrl);
        var br = JsonNode.Parse(api.Requests[0].Body!)!["billing_requests"]!;
        Assert.Equal("bacs", br["mandate_request"]!["scheme"]!.GetValue<string>());
        Assert.Equal("MS|m1", br["metadata"]!["lbc_ref"]!.GetValue<string>());
        Assert.Equal("2500", br["metadata"]!["lbc_amount"]!.GetValue<string>());
        var flow = JsonNode.Parse(api.Requests[1].Body!)!["billing_request_flows"]!;
        Assert.Equal("BRQ1", flow["links"]!["billing_request"]!.GetValue<string>());
        Assert.Equal("Aman Singh", flow["prefilled_customer"]!["given_name"]!.GetValue<string>());
        Assert.Equal("Gill", flow["prefilled_customer"]!["family_name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Bad_signature_is_rejected()
    {
        var req = Webhook(new { id = "EV1" }) with { Headers = new Dictionary<string, string> { [GoCardlessPaymentGateway.SignatureHeader] = "00" } };
        await Assert.ThrowsAsync<InvalidWebhookException>(() => Gateway(new StubApi()).ParseWebhookAsync(req));
    }

    [Fact]
    public async Task Fulfilled_billing_request_starts_monthly_subscription_once()
    {
        var api = new StubApi()
            .On("GET /billing_requests/BRQ1", new { billing_requests = new { id = "BRQ1", metadata = new { lbc_ref = "MS|m1", lbc_amount = "3500", lbc_name = "Silver" }, links = new { mandate_request_mandate = "MD1" } } })
            .On("POST /subscriptions", new { subscriptions = new { id = "SB1" } });

        var events = await Gateway(api).ParseWebhookAsync(Webhook(
            new { id = "EV1", resource_type = "billing_requests", action = "fulfilled", links = new { billing_request = "BRQ1" } }));

        var e = Assert.Single(events);
        Assert.Equal((PaymentEventType.RecurringStarted, "MS|m1", "SB1"), (e.Type, e.Reference, e.GatewayRef));
        var create = api.Requests.Single(r => r.Key == "POST /subscriptions");
        Assert.Equal("lbc-sub-MD1", create.IdempotencyKey);
        var sub = JsonNode.Parse(create.Body!)!["subscriptions"]!;
        Assert.Equal((3500, "monthly", "MD1"), (sub["amount"]!.GetValue<int>(), sub["interval_unit"]!.GetValue<string>(), sub["links"]!["mandate"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Retried_fulfilment_reuses_the_existing_subscription()
    {
        var api = new StubApi()
            .On("GET /billing_requests/BRQ1", new { billing_requests = new { metadata = new { lbc_ref = "MS|m1", lbc_amount = "2500" }, links = new { mandate_request_mandate = "MD1" } } })
            .On("POST /subscriptions", new { error = new { message = "conflict", errors = new[] { new { reason = "idempotent_creation_conflict", links = new { conflicting_resource_id = "SB1" } } } } }, HttpStatusCode.Conflict);

        var e = Assert.Single(await Gateway(api).ParseWebhookAsync(Webhook(
            new { id = "EV1", resource_type = "billing_requests", action = "fulfilled", links = new { billing_request = "BRQ1" } })));
        Assert.Equal("SB1", e.GatewayRef);
    }

    [Fact]
    public async Task Payment_and_cancellation_events_map_to_membership_events()
    {
        var api = new StubApi()
            .On("GET /payments/PM1", new { payments = new { amount = 2500, charge_date = "2026-12-04", links = new { subscription = "SB1" } } })
            .On("GET /payments/PM2", new { payments = new { amount = 2500, charge_date = "2027-01-04", links = new { subscription = "SB1" } } })
            .On("GET /payments/PM3", new { payments = new { amount = 750, links = new { mandate = "MD9" } } })
            .On("GET /subscriptions/SB1", new { subscriptions = new { metadata = new { lbc_ref = "MS|m1" } } })
            .On("GET /mandates/MD1", new { mandates = new { metadata = new { lbc_ref = "MS|m1" } } });

        var events = await Gateway(api).ParseWebhookAsync(Webhook(
            new { id = "EV1", resource_type = "payments", action = "confirmed", links = new { payment = "PM1" } },
            new { id = "EV2", resource_type = "payments", action = "failed", links = new { payment = "PM2" } },
            new { id = "EV3", resource_type = "payments", action = "confirmed", links = new { payment = "PM3" } }, // not a subscription: ignored
            new { id = "EV4", resource_type = "payments", action = "paid_out", links = new { payment = "PM1" } },  // not used: ignored
            new { id = "EV5", resource_type = "subscriptions", action = "cancelled", links = new { subscription = "SB1" } },
            new { id = "EV6", resource_type = "mandates", action = "cancelled", links = new { mandate = "MD1" } }));

        Assert.Equal(
            [("EV1", PaymentEventType.RecurringRenewed), ("EV2", PaymentEventType.RecurringFailed),
             ("EV5", PaymentEventType.RecurringCancelled), ("EV6", PaymentEventType.RecurringCancelled)],
            events.Select(e => (e.EventId, e.Type)));
        Assert.All(events, e => Assert.Equal("MS|m1", e.Reference));
        Assert.Equal(new DateTimeOffset(2027, 1, 4, 0, 0, 0, TimeSpan.Zero), events[0].PeriodEnd);
        Assert.Null(events[3].GatewayRef); // mandate event keeps the stored subscription id
    }

    [Fact]
    public async Task Cancelling_an_already_cancelled_subscription_is_not_an_error()
    {
        var api = new StubApi().On("POST /subscriptions/SB1/actions/cancel",
            new { error = new { message = "not active", errors = new[] { new { reason = "cancellation_failed" } } } }, HttpStatusCode.UnprocessableEntity);
        await Gateway(api).CancelRecurringAsync("SB1");
    }

    [Fact]
    public async Task Subscription_started_after_admin_cancelled_is_stopped_at_the_gateway()
    {
        var app = new TestApp();
        var m = new Membership { Id = "m1", Email = "a@example.com", Tier = MembershipTier.Silver, Status = MembershipStatus.Cancelled, MonthlyPricePence = 3500 };
        await app.Memberships.UpsertAsync(m);

        await app.Processor.HandleAsync(new PaymentEvent("EV1", PaymentEventType.RecurringStarted, "MS|m1", "SB1"));

        Assert.Equal(["SB1"], app.Gateway.Cancelled);
        Assert.Equal(MembershipStatus.Cancelled, (await app.Memberships.GetAsync("m1"))!.Status);
    }
}
