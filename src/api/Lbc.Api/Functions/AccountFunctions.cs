using Lbc.Api.Http;
using Lbc.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Lbc.Api.Functions;

public sealed record LoginBody(string? Email, string? Password);
public sealed record SetPasswordBody(string? Token, string? Password);
public sealed record ForgotBody(string? Email);

public sealed class AccountFunctions(ApiHost api, AccountService accounts, MemberAdminService members)
{
    [Function("Login")]
    public Task<IActionResult> Login([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequest req) =>
        api.Run(async () =>
        {
            var b = await ApiHost.ReadJson<LoginBody>(req);
            var user = await accounts.LoginAsync(b.Email, b.Password, req.HttpContext.RequestAborted);
            if (user is null) return ApiHost.Error(StatusCodes.Status401Unauthorized, "Email or password is incorrect.");
            api.SignIn(req.HttpContext.Response, user);
            return new OkObjectResult(new { name = user.Name, role = user.Role.ToString() });
        });

    [Function("Logout")]
    public Task<IActionResult> Logout([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/logout")] HttpRequest req) =>
        api.Run(() =>
        {
            ApiHost.SignOut(req.HttpContext.Response);
            return Task.FromResult<IActionResult>(new OkObjectResult(new { ok = true }));
        });

    /// <summary>Used by both the "create your account" and "reset password" links. Signs the user in on success.</summary>
    [Function("SetPassword")]
    public Task<IActionResult> SetPassword([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/set-password")] HttpRequest req) =>
        api.Run(async () =>
        {
            var b = await ApiHost.ReadJson<SetPasswordBody>(req);
            var user = await accounts.SetPasswordAsync(b.Token, b.Password, req.HttpContext.RequestAborted);
            api.SignIn(req.HttpContext.Response, user);
            return new OkObjectResult(new { name = user.Name, role = user.Role.ToString() });
        });

    [Function("ForgotPassword")]
    public Task<IActionResult> Forgot([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/forgot")] HttpRequest req) =>
        api.Run(async () =>
        {
            var b = await ApiHost.ReadJson<ForgotBody>(req);
            await accounts.RequestPasswordResetAsync(b.Email, req.HttpContext.RequestAborted);
            return new OkObjectResult(new { ok = true });
        });

    [Function("GetMe")]
    public Task<IActionResult> Me([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")] HttpRequest req) =>
        api.Run(async () =>
        {
            var user = api.RequireUser(req);
            return new OkObjectResult(await members.GetAccountAsync(user.Email, req.HttpContext.RequestAborted));
        });
}
