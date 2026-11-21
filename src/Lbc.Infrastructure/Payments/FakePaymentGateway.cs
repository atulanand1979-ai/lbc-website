using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lbc.Core;
using Lbc.Core.Payments;
using Microsoft.Extensions.Options;

namespace Lbc.Infrastructure.Payments;

public sealed class PaymentOptions
{
    public const string Section = "Payments";
    /// <summary>"Fake" for local development. A real provider adapter will be added once the client chooses one.</summary>
    public string Provider { get; set; } = "None";
    /// <summary>Shared secret the fake checkout page uses to sign its simulated webhooks.</summary>
    public string FakeWebhookSecret { get; set; } = "";
}

/// <summary>
/// Development stand-in for a real gateway. "Checkout" is a local page (/dev/fake-pay.html) whose
/// Pay button posts a signed webhook back to /api/payments/webhook, exercising the real flow end to end.
/// </summary>
public sealed class FakePaymentGateway(IOptions<PaymentOptions> options, IOptions<SiteOptions> site) : IPaymentGateway
{
    public const string SignatureHeader = "x-fake-signature";

    public Task<CheckoutSession> CreateOneOffCheckoutAsync(OneOffPaymentRequest r, CancellationToken ct = default) =>
        Task.FromResult(Checkout("one-off", r.Reference, r.AmountPence, r.Description, r.SuccessUrl, r.CancelUrl));

    public Task<CheckoutSession> CreateRecurringCheckoutAsync(RecurringPaymentRequest r, CancellationToken ct = default) =>
        Task.FromResult(Checkout("recurring", r.Reference, r.MonthlyAmountPence, r.Description, r.SuccessUrl, r.CancelUrl));

    public Task CancelRecurringAsync(string gatewayRecurringRef, CancellationToken ct = default) => Task.CompletedTask;

    public Task<PaymentEvent?> ParseWebhookAsync(WebhookRequest request, CancellationToken ct = default)
    {
        var secret = options.Value.FakeWebhookSecret;
        if (string.IsNullOrEmpty(secret)) throw new InvalidWebhookException("Fake gateway secret not configured.");
        if (!request.Headers.TryGetValue(SignatureHeader, out var sig) || !SignatureMatches(secret, request.Body, sig))
            throw new InvalidWebhookException("Bad signature.");

        var p = JsonSerializer.Deserialize<FakeWebhook>(request.Body, JsonSerializerOptions.Web)
                ?? throw new InvalidWebhookException("Empty body.");
        if (!Enum.TryParse<PaymentEventType>(p.Type, out var type)) return Task.FromResult<PaymentEvent?>(null);

        DateTimeOffset? periodEnd = type is PaymentEventType.RecurringStarted or PaymentEventType.RecurringRenewed
            ? DateTimeOffset.UtcNow.AddMonths(1) : null;
        var gatewayRef = type == PaymentEventType.OneOffPaid ? $"fake_pay_{p.EventId}" : $"fake_sub_{Lbc.Core.Services.Ids.Sha256(p.Reference)[..16]}";
        return Task.FromResult<PaymentEvent?>(new PaymentEvent(p.EventId, type, p.Reference, gatewayRef, p.AmountPence, periodEnd));
    }

    public static string Sign(string secret, string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private static bool SignatureMatches(string secret, string body, string signature) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(secret, body)), Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));

    private CheckoutSession Checkout(string kind, string reference, int amountPence, string description, string successUrl, string cancelUrl)
    {
        var q = string.Join("&", new Dictionary<string, string>
        {
            ["kind"] = kind, ["ref"] = reference, ["amount"] = amountPence.ToString(), ["desc"] = description,
            ["success"] = successUrl, ["cancel"] = cancelUrl,
        }.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        return new CheckoutSession($"{site.Value.BaseUrl.TrimEnd('/')}/dev/fake-pay.html?{q}", null);
    }

    private sealed record FakeWebhook(string EventId, string Type, string Reference, int? AmountPence);
}

/// <summary>Used when no provider is configured: refuses to take payments rather than pretending to.</summary>
public sealed class UnconfiguredPaymentGateway : IPaymentGateway
{
    private static Exception NotConfigured() => new InvalidOperationException("No payment provider is configured (Payments:Provider).");
    public Task<CheckoutSession> CreateOneOffCheckoutAsync(OneOffPaymentRequest r, CancellationToken ct = default) => throw NotConfigured();
    public Task<CheckoutSession> CreateRecurringCheckoutAsync(RecurringPaymentRequest r, CancellationToken ct = default) => throw NotConfigured();
    public Task CancelRecurringAsync(string gatewayRecurringRef, CancellationToken ct = default) => throw NotConfigured();
    public Task<PaymentEvent?> ParseWebhookAsync(WebhookRequest request, CancellationToken ct = default) => throw new InvalidWebhookException("No payment provider configured.");
}
