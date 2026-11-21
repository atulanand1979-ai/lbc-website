using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace Lbc.Core.Services;

/// <summary>A user-correctable problem (bad input, business rule). The API maps it to HTTP 400.</summary>
public sealed class ValidationException(string message) : Exception(message);

/// <summary>The thing asked for does not exist. The API maps it to HTTP 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);

public static class Guard
{
    public static string Email(string? email)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        if (e.Length is 0 or > 254 || !MailAddress.TryCreate(e, out var parsed) || parsed.Address != e || !e.Contains('.'))
            throw new ValidationException("Please enter a valid email address.");
        return e;
    }

    public static string Required(string? value, string field, int maxLength = 200)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) throw new ValidationException($"{field} is required.");
        if (v.Length > maxLength) throw new ValidationException($"{field} must be {maxLength} characters or fewer.");
        return v;
    }

    public static string Optional(string? value, string field, int maxLength = 200)
    {
        var v = (value ?? "").Trim();
        if (v.Length > maxLength) throw new ValidationException($"{field} must be {maxLength} characters or fewer.");
        return v;
    }

    public static string Phone(string? phone, bool required)
    {
        var p = (phone ?? "").Trim();
        if (p.Length == 0)
        {
            if (required) throw new ValidationException("Phone number is required.");
            return p;
        }
        var digits = p.Count(char.IsDigit);
        if (digits is < 10 or > 15 || p.Any(c => !(char.IsDigit(c) || c is ' ' or '+' or '-' or '(' or ')')))
            throw new ValidationException("Please enter a valid phone number.");
        return p;
    }

    public static string Password(string? password)
    {
        if (password is null || password.Length < 10)
            throw new ValidationException("Password must be at least 10 characters.");
        if (password.Length > 200) throw new ValidationException("Password is too long.");
        return password;
    }

    public static string Slug(string? value)
    {
        var s = (value ?? "").Trim().ToLowerInvariant();
        if (s.Length is 0 or > 50 || !s.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') || s.StartsWith('-') || s.EndsWith('-'))
            throw new ValidationException("Slug must be 1–50 characters: lower-case letters, numbers and hyphens.");
        return s;
    }
}

public static class Ids
{
    /// <summary>Short, URL-safe random id.</summary>
    public static string New() => Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();

    public static string NewSecretToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public static class Money
{
    public static string Format(int pence) => pence % 100 == 0 ? $"£{pence / 100}" : $"£{pence / 100m:0.00}";
}
