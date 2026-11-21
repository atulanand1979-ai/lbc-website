using System.Net.Http.Json;
using Lbc.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace Lbc.Infrastructure;

public sealed class TurnstileOptions
{
    public const string Section = "Turnstile";
    /// <summary>Cloudflare Turnstile secret. Empty = verification disabled (local development).</summary>
    public string SecretKey { get; set; } = "";
    public string SiteKey { get; set; } = "";
}

public sealed class TurnstileVerifier(HttpClient http, IOptions<TurnstileOptions> options) : ICaptchaVerifier
{
    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default)
    {
        var secret = options.Value.SecretKey;
        if (string.IsNullOrEmpty(secret)) return true;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var form = new Dictionary<string, string> { ["secret"] = secret, ["response"] = token };
        if (!string.IsNullOrEmpty(remoteIp)) form["remoteip"] = remoteIp;
        using var resp = await http.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", new FormUrlEncodedContent(form), ct);
        if (!resp.IsSuccessStatusCode) return false;
        var body = await resp.Content.ReadFromJsonAsync<SiteVerify>(ct);
        return body?.Success == true;
    }

    private sealed record SiteVerify(bool Success);
}
