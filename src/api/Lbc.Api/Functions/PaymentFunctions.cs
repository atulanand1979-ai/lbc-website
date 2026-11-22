using System.Text.Json;
using Lbc.Api.Http;
using Lbc.Core.Payments;
using Lbc.Core.Services;
using Lbc.Infrastructure.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lbc.Api.Functions;

public sealed record FakePayBody(string? Reference, string? Kind, int? AmountPence, string? Outcome);

public sealed class PaymentFunctions(
    ApiHost api,
    IPaymentGateway gateway,
    PaymentEventProcessor processor,
    IOptions<PaymentOptions> paymentOptions,
    ILogger<PaymentFunctions> logger)
{
    /// <summary>Receives the gateway's webhooks. The gateway retries on any non-2xx response.</summary>
    [Function("PaymentWebhook")]
    public async Task<IActionResult> Webhook([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "payments/webhook")] HttpRequest req)
    {
        using var reader = new StreamReader(req.Body);
        var body = await reader.ReadToEndAsync(req.HttpContext.RequestAborted);
        var headers = req.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString());

        IReadOnlyList<PaymentEvent> events;
        try { events = await gateway.ParseWebhookAsync(new WebhookRequest(body, headers), req.HttpContext.RequestAborted); }
        catch (InvalidWebhookException ex)
        {
            logger.LogWarning("Rejected payment webhook: {Reason}", ex.Message);
            return new BadRequestResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed reading payment webhook");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError); // the gateway retries
        }

        // Events already handled are skipped by the processor, so a retried batch is safe.
        foreach (var evt in events)
        {
            try { await processor.HandleAsync(evt, req.HttpContext.RequestAborted); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed processing payment event {EventId}", evt.EventId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
        return new OkResult();
    }

    /// <summary>
    /// Development only (Payments:Provider = Fake): the fake checkout page calls this to simulate the
    /// gateway. It signs a webhook server-side and sends it through the same parse + process path.
    /// </summary>
    [Function("FakePay")]
    public Task<IActionResult> FakePay([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dev/fake-pay")] HttpRequest req) =>
        api.Run(async () =>
        {
            if (gateway is not FakePaymentGateway) return new NotFoundResult();
            var b = await ApiHost.ReadJson<FakePayBody>(req);
            var type = (b.Kind, b.Outcome) switch
            {
                ("one-off", _) => PaymentEventType.OneOffPaid,
                ("recurring", "renew") => PaymentEventType.RecurringRenewed,
                ("recurring", "fail") => PaymentEventType.RecurringFailed,
                ("recurring", "cancel") => PaymentEventType.RecurringCancelled,
                ("recurring", _) => PaymentEventType.RecurringStarted,
                _ => throw new ValidationException("Unknown payment kind."),
            };
            var payload = JsonSerializer.Serialize(new
            {
                eventId = $"evt_{Ids.New()}", type = type.ToString(), reference = b.Reference ?? "", amountPence = b.AmountPence,
            }, JsonSerializerOptions.Web);
            var headers = new Dictionary<string, string>
            {
                [FakePaymentGateway.SignatureHeader] = FakePaymentGateway.Sign(paymentOptions.Value.FakeWebhookSecret, payload),
            };
            foreach (var evt in await gateway.ParseWebhookAsync(new WebhookRequest(payload, headers)))
                await processor.HandleAsync(evt, req.HttpContext.RequestAborted);
            return new OkObjectResult(new { ok = true, type = type.ToString() });
        });
}
