using Lbc.Api.Http;
using Lbc.Core.Abstractions;
using Lbc.Core.Services;
using Lbc.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;

namespace Lbc.Api.Functions;

public sealed record EnquiryBody(string? Type, string? Name, string? Email, string? Phone, string? Topic, string? EventDate, string? Message, string? CaptchaToken);
public sealed record PayAsYouGoBody(string? VenueSlug, string? ClassId, string? Date, string? Name, string? Email, string? Phone, string? CaptchaToken);
public sealed record MembershipBody(string? Tier, string? HomeVenueSlug, string? Name, string? Email, string? Phone, string? CaptchaToken);

public sealed class PublicFunctions(
    ApiHost api,
    CatalogService catalog,
    CheckoutService checkout,
    EnquiryService enquiries,
    ICaptchaVerifier captcha,
    IOptions<TurnstileOptions> turnstile)
{
    /// <summary>Prices and the public Turnstile site key, for the browser.</summary>
    [Function("GetConfig")]
    public Task<IActionResult> GetConfig([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config")] HttpRequest req) =>
        api.Run(() => Task.FromResult<IActionResult>(new OkObjectResult(new
        {
            prices = catalog.Prices(),
            captchaSiteKey = string.IsNullOrEmpty(turnstile.Value.SiteKey) ? null : turnstile.Value.SiteKey,
        })));

    [Function("GetVenues")]
    public Task<IActionResult> GetVenues([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "venues")] HttpRequest req) =>
        api.Run(async () => new OkObjectResult(await catalog.GetVenuesAsync(includeInactive: false, req.HttpContext.RequestAborted)));

    [Function("GetClassDates")]
    public Task<IActionResult> GetClassDates(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "venues/{slug}/classes/{classId}/dates")] HttpRequest req,
        string slug, string classId) =>
        api.Run(async () => new OkObjectResult(await catalog.GetUpcomingDatesAsync(slug, classId, req.HttpContext.RequestAborted)));

    [Function("CreateBooking")]
    public Task<IActionResult> CreateBooking([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bookings")] HttpRequest req) =>
        api.Run(async () =>
        {
            var b = await ApiHost.ReadJson<PayAsYouGoBody>(req);
            await RequireCaptcha(req, b.CaptchaToken);
            var session = await checkout.StartPayAsYouGoAsync(new(b.VenueSlug, b.ClassId, b.Date, b.Name, b.Email, b.Phone), req.HttpContext.RequestAborted);
            return new OkObjectResult(new { redirectUrl = session.RedirectUrl });
        });

    [Function("CreateMembership")]
    public Task<IActionResult> CreateMembership([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "memberships")] HttpRequest req) =>
        api.Run(async () =>
        {
            var b = await ApiHost.ReadJson<MembershipBody>(req);
            await RequireCaptcha(req, b.CaptchaToken);
            var session = await checkout.StartMembershipAsync(new(b.Tier, b.HomeVenueSlug, b.Name, b.Email, b.Phone), req.HttpContext.RequestAborted);
            return new OkObjectResult(new { redirectUrl = session.RedirectUrl });
        });

    [Function("CreateEnquiry")]
    public Task<IActionResult> CreateEnquiry([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "enquiries")] HttpRequest req) =>
        api.Run(async () =>
        {
            var b = await ApiHost.ReadJson<EnquiryBody>(req);
            await RequireCaptcha(req, b.CaptchaToken);
            await enquiries.SubmitAsync(new(b.Type, b.Name, b.Email, b.Phone, b.Topic, b.EventDate, b.Message), req.HttpContext.RequestAborted);
            return new OkObjectResult(new { ok = true });
        });

    private async Task RequireCaptcha(HttpRequest req, string? token)
    {
        if (!await captcha.VerifyAsync(token, ApiHost.ClientIp(req), req.HttpContext.RequestAborted))
            throw new ValidationException("Please complete the anti-spam check and try again.");
    }
}
