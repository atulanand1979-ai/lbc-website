using System.Security.Claims;
using System.Text;
using Lbc.Core.Abstractions;
using Lbc.Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Lbc.Infrastructure;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    /// <summary>HMAC signing key, at least 32 random bytes (base64 or plain text). Keep it in app settings, never in source.</summary>
    public string SigningKey { get; set; } = "";
    public string Issuer { get; set; } = "lbc";
    public string Audience { get; set; } = "lbc-web";
    public int LifetimeDays { get; set; } = 7;
}

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _key;
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly TimeProvider _clock;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
        if (keyBytes.Length < 32) throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");
        _key = new SymmetricSecurityKey(keyBytes);
    }

    public TimeSpan Lifetime => TimeSpan.FromDays(_options.LifetimeDays);

    public string Issue(User user)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(Lifetime),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity([
                new Claim("sub", user.UserId),
                new Claim("email", user.Email),
                new Claim("name", user.Name),
                new Claim("role", user.Role.ToString()),
            ]),
        });
    }

    public AuthUser? Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var result = _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = _options.Issuer,
            ValidAudience = _options.Audience,
            IssuerSigningKey = _key,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromMinutes(1),
            LifetimeValidator = (nbf, exp, _, _) =>
            {
                var now = _clock.GetUtcNow().UtcDateTime;
                return (nbf is null || nbf <= now.AddMinutes(1)) && exp is not null && exp > now;
            },
        }).GetAwaiter().GetResult();
        if (!result.IsValid) return null;

        var email = result.Claims.TryGetValue("email", out var e) ? e as string : null;
        var name = result.Claims.TryGetValue("name", out var n) ? n as string : null;
        var role = result.Claims.TryGetValue("role", out var r) && Enum.TryParse<UserRole>(r as string, out var parsed) ? parsed : UserRole.Member;
        return email is null ? null : new AuthUser(email, name ?? "", role);
    }
}

/// <summary>ASP.NET Core Identity's PBKDF2 hasher (versioned format, upgradeable).</summary>
public sealed class PasswordService : IPasswordService
{
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object Subject = new();

    public string Hash(string password) => Hasher.HashPassword(Subject, password);

    public bool Verify(string hash, string password) =>
        Hasher.VerifyHashedPassword(Subject, hash, password) != PasswordVerificationResult.Failed;
}
