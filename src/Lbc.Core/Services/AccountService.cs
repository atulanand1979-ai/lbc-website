using Lbc.Core.Abstractions;
using Lbc.Core.Domain;
using Microsoft.Extensions.Options;

namespace Lbc.Core.Services;

public sealed class AccountService(
    IUserRepository users,
    IAccountTokenRepository tokens,
    IPasswordService passwords,
    IEmailSender email,
    IOptions<SiteOptions> site,
    TimeProvider clock)
{
    public static readonly TimeSpan SetPasswordLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    /// <summary>Gets the user, creating a password-less one if they don't exist yet.</summary>
    public async Task<User> EnsureUserAsync(string email, string name, string phone, CancellationToken ct = default)
    {
        var existing = await users.GetAsync(email, ct);
        if (existing is not null)
        {
            if (string.IsNullOrEmpty(existing.Phone) && phone.Length > 0)
            {
                existing.Phone = phone;
                await users.UpdateAsync(existing, ct);
            }
            return existing;
        }

        var user = new User { Email = email, UserId = Ids.New(), Name = name, Phone = phone, CreatedAt = clock.GetUtcNow() };
        if (await users.TryAddAsync(user, ct)) return user;
        // Lost a race with a concurrent request: use the winner.
        return await users.GetAsync(email, ct) ?? throw new InvalidOperationException("User vanished after insert conflict.");
    }

    /// <summary>Creates a one-time link and returns its URL (does not send it).</summary>
    public async Task<string> CreateLinkAsync(string email, AccountTokenPurpose purpose, CancellationToken ct = default)
    {
        var raw = Ids.NewSecretToken();
        var lifetime = purpose == AccountTokenPurpose.ResetPassword ? ResetLifetime : SetPasswordLifetime;
        await tokens.AddAsync(new AccountToken
        {
            TokenHash = Ids.Sha256(raw),
            Email = email,
            Purpose = purpose,
            ExpiresAt = clock.GetUtcNow().Add(lifetime),
        }, ct);
        return $"{site.Value.BaseUrl.TrimEnd('/')}/account/set-password.html?token={Uri.EscapeDataString(raw)}";
    }

    /// <summary>Always succeeds from the caller's point of view so it can't be used to discover accounts.</summary>
    public async Task RequestPasswordResetAsync(string? rawEmail, CancellationToken ct = default)
    {
        string emailAddress;
        try { emailAddress = Guard.Email(rawEmail); }
        catch (ValidationException) { return; }

        var user = await users.GetAsync(emailAddress, ct);
        if (user is null) return;

        var purpose = user.HasPassword ? AccountTokenPurpose.ResetPassword : AccountTokenPurpose.SetPassword;
        var url = await CreateLinkAsync(user.Email, purpose, ct);
        await email.SendAsync(EmailTemplates.SetPassword(site.Value, user.Email, url, user.HasPassword), ct);
    }

    public async Task<User> SetPasswordAsync(string? rawToken, string? password, CancellationToken ct = default)
    {
        var pwd = Guard.Password(password);
        if (string.IsNullOrWhiteSpace(rawToken)) throw new ValidationException("This link is invalid or has expired.");

        var token = await tokens.GetAsync(Ids.Sha256(rawToken.Trim()), ct);
        if (token is null || token.UsedAt is not null || token.ExpiresAt <= clock.GetUtcNow())
            throw new ValidationException("This link is invalid or has expired. Please request a new one.");

        var user = await users.GetAsync(token.Email, ct)
                   ?? throw new ValidationException("This link is invalid or has expired.");

        token.UsedAt = clock.GetUtcNow();
        await tokens.UpdateAsync(token, ct);

        user.PasswordHash = passwords.Hash(pwd);
        await users.UpdateAsync(user, ct);
        return user;
    }

    /// <summary>Returns the user if the credentials are valid, otherwise null.</summary>
    public async Task<User?> LoginAsync(string? rawEmail, string? password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(rawEmail)) return null;
        var user = await users.GetAsync(rawEmail.Trim().ToLowerInvariant(), ct);
        if (user?.PasswordHash is null)
        {
            passwords.Hash(password); // keep timing similar whether or not the account exists
            return null;
        }
        return passwords.Verify(user.PasswordHash, password) ? user : null;
    }
}
