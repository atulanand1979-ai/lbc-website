namespace Lbc.Core.Payments;

/// <summary>
/// Provider-neutral payment gateway. Implementations use the provider's hosted checkout
/// (no card data passes through our code) and translate the provider's webhooks into
/// <see cref="PaymentEvent"/>s. All business data stays in our own tables; the gateway
/// only holds what it must to take money (e.g. the recurring mandate).
/// </summary>
public interface IPaymentGateway
{
    /// <summary>One-off payment (Pay As You Go class).</summary>
    Task<CheckoutSession> CreateOneOffCheckoutAsync(OneOffPaymentRequest request, CancellationToken ct = default);

    /// <summary>Monthly recurring payment (membership).</summary>
    Task<CheckoutSession> CreateRecurringCheckoutAsync(RecurringPaymentRequest request, CancellationToken ct = default);

    Task CancelRecurringAsync(string gatewayRecurringRef, CancellationToken ct = default);

    /// <summary>
    /// Verifies the webhook's signature and maps it to normalised events (a provider may batch several
    /// events in one webhook). Event types we don't use are left out. Throws <see cref="InvalidWebhookException"/>
    /// if the signature is invalid. May call the provider (e.g. to look up our reference, or to start the
    /// monthly collection once a Direct Debit mandate exists); such calls must be idempotent because webhooks are retried.
    /// </summary>
    Task<IReadOnlyList<PaymentEvent>> ParseWebhookAsync(WebhookRequest request, CancellationToken ct = default);
}

/// <param name="Reference">Our reference (see <see cref="PaymentReference"/>). Gateways must echo it back on every event.</param>
public sealed record OneOffPaymentRequest(
    string Reference, int AmountPence, string Description,
    string CustomerEmail, string CustomerName, string SuccessUrl, string CancelUrl);

public sealed record RecurringPaymentRequest(
    string Reference, int MonthlyAmountPence, string Description,
    string CustomerEmail, string CustomerName, string SuccessUrl, string CancelUrl);

public sealed record CheckoutSession(string RedirectUrl, string? GatewaySessionRef);

/// <summary>Raw webhook as received over HTTP.</summary>
public sealed record WebhookRequest(string Body, IReadOnlyDictionary<string, string> Headers);

public enum PaymentEventType
{
    OneOffPaid,
    RecurringStarted,
    RecurringRenewed,
    RecurringFailed,
    RecurringCancelled,
}

/// <param name="EventId">Gateway's unique event id, used for idempotency.</param>
/// <param name="Reference">Our reference originally passed to the checkout.</param>
/// <param name="GatewayRef">Gateway's payment id (one-off) or subscription/mandate id (recurring).</param>
/// <param name="PeriodEnd">For recurring events, when the current paid period ends.</param>
public sealed record PaymentEvent(
    string EventId,
    PaymentEventType Type,
    string Reference,
    string? GatewayRef,
    int? AmountPence = null,
    DateTimeOffset? PeriodEnd = null);

public sealed class InvalidWebhookException(string message) : Exception(message);

/// <summary>
/// Encodes our own reference passed to the gateway so the webhook can find the record directly:
/// bookings: <c>BK|{classId}|{yyyyMMdd}|{bookingId}</c>, memberships: <c>MS|{membershipId}</c>.
/// </summary>
public static class PaymentReference
{
    public static string ForBooking(string classId, DateOnly date, string bookingId) =>
        $"BK|{classId}|{date:yyyyMMdd}|{bookingId}";

    public static string ForMembership(string membershipId) => $"MS|{membershipId}";

    public static bool TryParseBooking(string reference, out string classId, out DateOnly date, out string bookingId)
    {
        classId = bookingId = "";
        date = default;
        var parts = reference.Split('|');
        if (parts.Length != 4 || parts[0] != "BK") return false;
        if (!DateOnly.TryParseExact(parts[2], "yyyyMMdd", out date)) return false;
        classId = parts[1];
        bookingId = parts[3];
        return classId.Length > 0 && bookingId.Length > 0;
    }

    public static bool TryParseMembership(string reference, out string membershipId)
    {
        membershipId = "";
        var parts = reference.Split('|');
        if (parts.Length != 2 || parts[0] != "MS" || parts[1].Length == 0) return false;
        membershipId = parts[1];
        return true;
    }
}
