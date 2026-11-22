using System.Security.Cryptography;
using System.Text;
using Lbc.Api.Http;
using Lbc.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;

namespace Lbc.Api.Functions;

public sealed record HomeVenueBody(string? VenueSlug);
public sealed record BootstrapBody(string? Email, string? Name);

/// <summary>Admin endpoints. Routes use "manage/" because Azure Functions reserves "admin/".</summary>
public sealed class ManageFunctions(
    ApiHost api,
    CatalogService catalog,
    ScheduleAdminService schedule,
    MemberAdminService members,
    SeedService seed,
    IConfiguration config)
{
    // ---- Places & weekly classes ----

    [Function("ManageGetVenues")]
    public Task<IActionResult> GetVenues([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/venues")] HttpRequest req) =>
        api.Run(async () =>
        {
            api.RequireAdmin(req);
            return new OkObjectResult(await catalog.GetVenuesAsync(includeInactive: true, req.HttpContext.RequestAborted));
        });

    [Function("ManageCreateVenue")]
    public Task<IActionResult> CreateVenue([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/venues")] HttpRequest req) =>
        api.Run(async () =>
        {
            api.RequireAdmin(req);
            var v = await schedule.CreateVenueAsync(await ApiHost.ReadJson<VenueInput>(req), req.HttpContext.RequestAborted);
            return new OkObjectResult(CatalogService.ToDto(v, []));
        });

    [Function("ManageUpdateVenue")]
    public Task<IActionResult> UpdateVenue([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/venues/{slug}")] HttpRequest req, string slug) =>
        api.Run(async () =>
        {
            api.RequireAdmin(req);
            var v = await schedule.UpdateVenueAsync(slug, await ApiHost.ReadJson<VenueInput>(req), req.HttpContext.RequestAborted);
            return new OkObjectResult(CatalogService.ToDto(v, []));
        });

    [Function("ManageDeleteVenue")]
    public Task<IActionResult> DeleteVenue([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/venues/{slug}")] HttpRequest req, string slug) =>
        api.Run(async () =>
        {
            api.RequireAdmin(req);
            await schedule.DeleteVenueAsync(slug, req.HttpContext.RequestAborted);
            return new OkObjectResult(new { ok = true });
        });

    [Function("ManageCreateClass")]
    public Task<IActionResult> CreateClass([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/venues/{slug}/classes")] HttpRequest req, string slug) =>
        api.Run(async () =>
        {
            api.RequireAdmin(req);
            var c = await schedule.CreateClassAsync(slug, await ApiHost.ReadJson<ClassInput>(req), req.HttpContext.RequestAborted);
            return new OkObjectResult(CatalogService.ToDto(c));
        });

    [Function("ManageUpdateClass")]
    public Task<IActionResult> UpdateClass(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/venues/{slug}/classes/{classId}")] HttpRequest req, string slug, string classId) =>
        api.Run(async () =>
        {
            api.RequireAdmin(req);
            var c = await schedule.UpdateClassAsync(slug, classId, await ApiHost.ReadJson<ClassInput>(req), req.HttpContext.RequestAborted);
            return new OkObjectResult(CatalogService.ToDto(c));
        });

    [Function("ManageDeleteClass")]
    public Task<IActionResult> DeleteClass(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/venues/{slug}/classes/{classId}")] HttpRequest req, string slug, string classId) =>
        api.Run(async () =>
        {
            api.RequireAdmin(req);
            await schedule.DeleteClassAsync(slug, classId, req.HttpContext.RequestAborted);
            return new OkObjectResult(new { ok = true });
        });

    [Function("ManageSeed")]
    public Task<IActionResult> Seed([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/seed")] HttpRequest req) =>
        api.Run(async () =>
        {
            api.RequireAdmin(req);
            var seeded = await seed.SeedIfEmptyAsync(req.HttpContext.RequestAborted);
            return new OkObjectResult(new { seeded });
        });

    // ---- Register, members, enquiries ----

    [Function("ManageRegister")]
    public Task<IActionResult> Register([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/register")] HttpRequest req) =>
        api.Run(async () =>
        {
            api.RequireAdmin(req);
            if (!DateOnly.TryParseExact(req.Query["date"].ToString(), "yyyy-MM-dd", out var date))
                throw new ValidationException("Please choose a date.");
            return new OkObjectResult(await members.GetRegisterAsync(req.Query["venue"].ToString(), date, req.HttpContext.RequestAborted));
        });

    [Function("ManageMemberships")]
    public Task<IActionResult> Memberships([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/memberships")] HttpRequest req) =>
        api.Run(async () =>
        {
            api.RequireAdmin(req);
            var includePending = req.Query["pending"] == "true";
            return new OkObjectResult(await members.GetMembershipsAsync(includePending, req.HttpContext.RequestAborted));
        });

    [Function("ManageMembershipHomeVenue")]
    public Task<IActionResult> HomeVenue(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/memberships/{id}/home-venue")] HttpRequest req, string id) =>
        api.Run(async () =>
        {
            api.RequireAdmin(req);
            var b = await ApiHost.ReadJson<HomeVenueBody>(req);
            return new OkObjectResult(await members.ChangeHomeVenueAsync(id, b.VenueSlug, req.HttpContext.RequestAborted));
        });

    [Function("ManageMembershipCancel")]
    public Task<IActionResult> Cancel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/memberships/{id}/cancel")] HttpRequest req, string id) =>
        api.Run(async () =>
        {
            api.RequireAdmin(req);
            return new OkObjectResult(await members.CancelMembershipAsync(id, req.HttpContext.RequestAborted));
        });

    [Function("ManageEnquiries")]
    public Task<IActionResult> Enquiries([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/enquiries")] HttpRequest req) =>
        api.Run(async () =>
        {
            api.RequireAdmin(req);
            var days = int.TryParse(req.Query["days"], out var d) ? d : 90;
            return new OkObjectResult(await members.GetEnquiriesAsync(days, req.HttpContext.RequestAborted));
        });

    /// <summary>
    /// One-off creation of the first admin. Requires the x-bootstrap-key header to match the
    /// ADMIN_BOOTSTRAP_KEY setting, and stops working once an admin exists.
    /// </summary>
    [Function("ManageBootstrap")]
    public Task<IActionResult> Bootstrap([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/bootstrap")] HttpRequest req) =>
        api.Run(async () =>
        {
            var expected = config["ADMIN_BOOTSTRAP_KEY"];
            var provided = req.Headers["x-bootstrap-key"].ToString();
            if (string.IsNullOrEmpty(expected) || expected.Length < 16 ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided)))
                return new NotFoundResult();
            var b = await ApiHost.ReadJson<BootstrapBody>(req);
            // The caller already holds the bootstrap secret, so returning the link grants nothing extra.
            var url = await members.BootstrapAdminAsync(b.Email, b.Name, req.HttpContext.RequestAborted);
            return new OkObjectResult(new { ok = true, setPasswordUrl = url, message = "Admin created. Open setPasswordUrl to choose a password." });
        });
}
