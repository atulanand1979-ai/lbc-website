using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lbc.Core.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lbc.Infrastructure.Payments;

/// <summary>
/// GoCardless (Bacs Direct Debit) for monthly memberships, using the REST API directly.
/// <list type="number">
/// <item>Checkout: create a Billing Request (mandate only) + Billing Request Flow and send the customer to
///   GoCardless's hosted page. Our reference and the monthly amount travel in the request's metadata.</item>
/// <item>Webhook <c>billing_requests.fulfilled</c>: the mandate exists, so we create the monthly Subscription
///   (idempotency key = mandate id, so retries never create two) and report <see cref="PaymentEventType.RecurringStarted"/>.</item>
/// <item><c>payments.confirmed</c> → renewed; <c>payments.failed/charged_back/late_failure_settled</c> → failed;
///   <c>subscriptions.cancelled/finished</c> and <c>mandates.cancelled/failed/expired</c> → cancelled.</item>
/// </list>
/// Webhook events carry only ids, so we read our reference back from the resource's metadata.
/// One-off payments are not offered (Pay As You Go is paid at the class).
/// </summary>
public sealed class GoCardlessPaymentGateway : IPaymentGateway
{
    public const string SignatureHeader = "webhook-signature";
    private const string RefKey = "lbc_ref", AmountKey = "lbc_amount", NameKey = "lbc_name";

    private readonly HttpClient http;
    private readonly GoCardlessOptions options;
    private readonly ILogger<GoCardlessPaymentGateway> logger;

    public GoCardlessPaymentGateway(HttpClient http, IOptions<PaymentOptions> options, ILogger<GoCardlessPaymentGateway> logger)
    {
        this.http = http;
        this.options = options.Value.GoCardless;
        this.logger = logger;
        http.BaseAddress = this.options.BaseAddress;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this.options.AccessToken);
        http.DefaultRequestHeaders.Add("GoCardless-Version", "2015-07-06");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<CheckoutSession> CreateOneOffCheckoutAsync(OneOffPaymentRequest request, CancellationToken ct = default) =>
        throw new InvalidOperationException("GoCardless is only used for memberships; Pay As You Go is paid at the class.");

    public async Task<CheckoutSession> CreateRecurringCheckoutAsync(RecurringPaymentRequest r, CancellationToken ct = default)
    {
        var metadata = new JsonObject
        {
            [RefKey] = r.Reference,
            [AmountKey] = r.MonthlyAmountPence.ToString(),
            [NameKey] = Truncate(r.Description, 255),
        };
        var billingRequest = await SendAsync(HttpMethod.Post, "billing_requests", new JsonObject
        {
            ["billing_requests"] = new JsonObject
            {
                ["mandate_request"] = new JsonObject
                {
                    ["scheme"] = "bacs",
                    ["metadata"] = new JsonObject { [RefKey] = r.Reference },
                },
                ["metadata"] = metadata,
            },
        }, idempotencyKey: $"lbc-br-{r.Reference}", ct);
        var billingRequestId = Str(billingRequest, "billing_requests", "id");

        var (given, family) = SplitName(r.CustomerName);
        var flow = await SendAsync(HttpMethod.Post, "billing_request_flows", new JsonObject
        {
            ["billing_request_flows"] = new JsonObject
            {
                ["redirect_uri"] = r.SuccessUrl,
                ["exit_uri"] = r.CancelUrl,
                ["links"] = new JsonObject { ["billing_request"] = billingRequestId },
                ["prefilled_customer"] = new JsonObject { ["given_name"] = given, ["family_name"] = family, ["email"] = r.CustomerEmail },
            },
        }, idempotencyKey: null, ct);
        return new CheckoutSession(Str(flow, "billing_request_flows", "authorisation_url"), billingRequestId);
    }

    public async Task CancelRecurringAsync(string subscriptionId, CancellationToken ct = default)
    {
        try
        {
            await SendAsync(HttpMethod.Post, $"subscriptions/{Uri.EscapeDataString(subscriptionId)}/actions/cancel",
                new JsonObject { ["data"] = new JsonObject() }, idempotencyKey: null, ct);
        }
        catch (GoCardlessApiException ex) when (ex.Reasons.Contains("cancellation_failed") || ex.Reasons.Contains("subscription_not_active"))
        {
            logger.LogInformation("GoCardless subscription {SubscriptionId} was already cancelled/finished", subscriptionId);
        }
    }

    public async Task<IReadOnlyList<PaymentEvent>> ParseWebhookAsync(WebhookRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(options.WebhookSecret)) throw new InvalidWebhookException("GoCardless webhook secret not configured.");
        if (!request.Headers.TryGetValue(SignatureHeader, out var sig) || !SignatureMatches(options.WebhookSecret, request.Body, sig))
            throw new InvalidWebhookException("Bad signature.");

        JsonArray events;
        try { events = JsonNode.Parse(request.Body)?["events"] as JsonArray ?? throw new InvalidWebhookException("No events."); }
        catch (JsonException) { throw new InvalidWebhookException("Body is not JSON."); }

        var result = new List<PaymentEvent>();
        foreach (var e in events)
        {
            if (e is null) continue;
            var mapped = await MapAsync(e, ct);
            if (mapped is not null) result.Add(mapped);
        }
        return result;
    }

    private async Task<PaymentEvent?> MapAsync(JsonNode e, CancellationToken ct)
    {
        var id = e["id"]?.GetValue<string>() ?? "";
        var resource = e["resource_type"]?.GetValue<string>();
        var action = e["action"]?.GetValue<string>();
        string? Link(string name) => e["links"]?[name]?.GetValue<string>();
        string Id(string name) => Uri.EscapeDataString(Link(name) ?? throw new InvalidOperationException($"GoCardless event {id} has no {name} link."));

        switch (resource, action)
        {
            case ("billing_requests", "fulfilled"):
            {
                var br = (await GetAsync($"billing_requests/{Id("billing_request")}", ct))["billing_requests"]!;
                var reference = br["metadata"]?[RefKey]?.GetValue<string>();
                var mandateId = br["links"]?["mandate_request_mandate"]?.GetValue<string>();
                if (reference is null || mandateId is null) return Ignore(id, "billing request without our reference or mandate");
                if (!int.TryParse(br["metadata"]?[AmountKey]?.GetValue<string>(), out var amount) || amount <= 0)
                    throw new InvalidOperationException($"Billing request {Link("billing_request")} has no amount in its metadata.");
                var name = br["metadata"]?[NameKey]?.GetValue<string>() ?? "Membership";
                var subscriptionId = await CreateSubscriptionAsync(mandateId, reference, amount, name, ct);
                return new PaymentEvent(id, PaymentEventType.RecurringStarted, reference, subscriptionId, amount);
            }

            case ("payments", "confirmed" or "failed" or "charged_back" or "late_failure_settled"):
            {
                var payment = (await GetAsync($"payments/{Id("payment")}", ct))["payments"]!;
                var subscriptionId = payment["links"]?["subscription"]?.GetValue<string>();
                if (subscriptionId is null) return Ignore(id, "payment not from a subscription");
                var reference = await SubscriptionReferenceAsync(subscriptionId, ct);
                if (reference is null) return Ignore(id, "subscription without our reference");
                var amount = payment["amount"]?.GetValue<int>();
                if (action != "confirmed")
                    return new PaymentEvent(id, PaymentEventType.RecurringFailed, reference, subscriptionId, amount);
                DateTimeOffset? periodEnd = DateOnly.TryParse(payment["charge_date"]?.GetValue<string>(), out var charged)
                    ? new DateTimeOffset(charged.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
                return new PaymentEvent(id, PaymentEventType.RecurringRenewed, reference, subscriptionId, amount, periodEnd);
            }

            case ("subscriptions", "cancelled" or "finished"):
            {
                var subscriptionId = Link("subscription") ?? throw new InvalidOperationException($"GoCardless event {id} has no subscription link.");
                var reference = await SubscriptionReferenceAsync(subscriptionId, ct);
                return reference is null ? Ignore(id, "subscription without our reference")
                    : new PaymentEvent(id, PaymentEventType.RecurringCancelled, reference, subscriptionId);
            }

            case ("mandates", "cancelled" or "failed" or "expired"):
            {
                var mandate = (await GetAsync($"mandates/{Id("mandate")}", ct))["mandates"]!;
                var reference = mandate["metadata"]?[RefKey]?.GetValue<string>();
                // GatewayRef null: keep the subscription id already stored on the membership.
                return reference is null ? Ignore(id, "mandate without our reference")
                    : new PaymentEvent(id, PaymentEventType.RecurringCancelled, reference, null);
            }

            default:
                return null;
        }
    }

    private PaymentEvent? Ignore(string eventId, string why)
    {
        logger.LogInformation("Ignoring GoCardless event {EventId}: {Why}", eventId, why);
        return null;
    }

    private async Task<string> CreateSubscriptionAsync(string mandateId, string reference, int amountPence, string name, CancellationToken ct)
    {
        try
        {
            var sub = await SendAsync(HttpMethod.Post, "subscriptions", new JsonObject
            {
                ["subscriptions"] = new JsonObject
                {
                    ["amount"] = amountPence,
                    ["currency"] = "GBP",
                    ["interval_unit"] = "monthly",
                    ["name"] = name,
                    ["metadata"] = new JsonObject { [RefKey] = reference },
                    ["links"] = new JsonObject { ["mandate"] = mandateId },
                },
            }, idempotencyKey: $"lbc-sub-{mandateId}", ct);
            return Str(sub, "subscriptions", "id");
        }
        catch (GoCardlessApiException ex) when (ex.ConflictingResourceId is { } existing)
        {
            return existing; // a retried webhook: the subscription was created last time
        }
    }

    private async Task<string?> SubscriptionReferenceAsync(string subscriptionId, CancellationToken ct) =>
        (await GetAsync($"subscriptions/{Uri.EscapeDataString(subscriptionId)}", ct))["subscriptions"]?["metadata"]?[RefKey]?.GetValue<string>();

    private Task<JsonNode> GetAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Get, path, null, null, ct);

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonObject? body, string? idempotencyKey, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(method, path);
        if (body is not null) msg.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        if (idempotencyKey is not null) msg.Headers.Add("Idempotency-Key", idempotencyKey);

        using var res = await http.SendAsync(msg, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (res.IsSuccessStatusCode) return JsonNode.Parse(text) ?? new JsonObject();

        var error = SafeParse(text)?["error"];
        var errors = error?["errors"] as JsonArray ?? [];
        var reasons = errors.Select(x => x?["reason"]?.GetValue<string>()).OfType<string>().ToHashSet();
        var conflicting = errors.Select(x => x?["links"]?["conflicting_resource_id"]?.GetValue<string>()).OfType<string>().FirstOrDefault();
        var message = error?["message"]?.GetValue<string>() ?? res.ReasonPhrase ?? "error";
        throw new GoCardlessApiException($"GoCardless {method} {path.Split('/')[0]} failed ({(int)res.StatusCode}): {message}",
            res.StatusCode, reasons, conflicting);
    }

    private static JsonNode? SafeParse(string text)
    {
        try { return JsonNode.Parse(text); } catch (JsonException) { return null; }
    }

    private static string Str(JsonNode node, string wrapper, string field) =>
        node[wrapper]?[field]?.GetValue<string>() ?? throw new InvalidOperationException($"GoCardless response missing {wrapper}.{field}.");

    private static (string Given, string Family) SplitName(string name)
    {
        var n = name.Trim();
        var i = n.LastIndexOf(' ');
        return i <= 0 ? (n, "") : (n[..i].Trim(), n[(i + 1)..]);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public static string Sign(string secret, string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private static bool SignatureMatches(string secret, string body, string signature) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(secret, body)), Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
}

public sealed class GoCardlessApiException(string message, HttpStatusCode status, IReadOnlySet<string> reasons, string? conflictingResourceId)
    : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public IReadOnlySet<string> Reasons { get; } = reasons;
    public string? ConflictingResourceId { get; } = conflictingResourceId;
}
