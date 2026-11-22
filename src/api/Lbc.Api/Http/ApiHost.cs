using System.Text.Json;
using Lbc.Core.Abstractions;
using Lbc.Core.Domain;
using Lbc.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Lbc.Api.Http;

public sealed class UnauthorizedException : Exception;
public sealed class ForbiddenException : Exception;

/// <summary>Shared request plumbing: JSON parsing, error mapping and cookie auth.</summary>
public sealed class ApiHost(ITokenService tokens, ILogger<ApiHost> logger)
{
    public const string AuthCookie = "lbc_session";

    /// <summary>Runs a handler and maps known exceptions to HTTP responses with a JSON { error } body.</summary>
    public async Task<IActionResult> Run(Func<Task<IActionResult>> handler)
    {
        try { return await handler(); }
        catch (ValidationException ex) { return Error(StatusCodes.Status400BadRequest, ex.Message); }
        catch (NotFoundException ex) { return Error(StatusCodes.Status404NotFound, ex.Message); }
        catch (UnauthorizedException) { return Error(StatusCodes.Status401Unauthorized, "Please sign in."); }
        catch (ForbiddenException) { return Error(StatusCodes.Status403Forbidden, "You don't have access to this."); }
        catch (BadJsonException ex) { return Error(StatusCodes.Status400BadRequest, ex.Message); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled API error");
            return Error(StatusCodes.Status500InternalServerError, "Sorry, something went wrong. Please try again or contact us.");
        }
    }

    public static IActionResult Error(int status, string message) =>
        new ObjectResult(new { error = message }) { StatusCode = status };

    /// <summary>
    /// Reads a JSON body. Requiring application/json also blocks cross-site form posts (CSRF):
    /// browsers can't send that content type cross-origin without a CORS preflight, which we never allow.
    /// </summary>
    public static async Task<T> ReadJson<T>(HttpRequest req)
    {
        if (!req.HasJsonContentType()) throw new BadJsonException("Expected a JSON request.");
        try
        {
            return await req.ReadFromJsonAsync<T>(JsonSerializerOptions.Web) ?? throw new BadJsonException("Empty request.");
        }
        catch (JsonException) { throw new BadJsonException("Invalid JSON request."); }
    }

    public AuthUser? CurrentUser(HttpRequest req) =>
        req.Cookies.TryGetValue(AuthCookie, out var token) && token is not null ? tokens.Validate(token) : null;

    public AuthUser RequireUser(HttpRequest req) => CurrentUser(req) ?? throw new UnauthorizedException();

    public AuthUser RequireAdmin(HttpRequest req)
    {
        var user = RequireUser(req);
        return user.Role == UserRole.Admin ? user : throw new ForbiddenException();
    }

    public void SignIn(HttpResponse res, User user) =>
        res.Cookies.Append(AuthCookie, tokens.Issue(user), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = tokens.Lifetime,
        });

    /// <summary>
    /// Overwrites the session cookie with a value that isn't a valid token (so the visitor is signed out)
    /// and no expiry. Deliberately NOT an expired cookie: on the live Functions host, Set-Cookie headers
    /// for already-expired cookies (Cookies.Delete, or Max-Age=0) never reach the browser.
    /// </summary>
    public static void SignOut(HttpResponse res) =>
        res.Cookies.Append(AuthCookie, "signed-out", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });

    public static string? ClientIp(HttpRequest req) =>
        req.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim() ?? req.HttpContext.Connection.RemoteIpAddress?.ToString();

    private sealed class BadJsonException(string message) : Exception(message);
}
