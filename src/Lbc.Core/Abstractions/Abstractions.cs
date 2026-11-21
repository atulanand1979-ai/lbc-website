using Lbc.Core.Domain;

namespace Lbc.Core.Abstractions;

public interface IVenueRepository
{
    Task<IReadOnlyList<Venue>> GetAllAsync(CancellationToken ct = default);
    Task<Venue?> GetAsync(string slug, CancellationToken ct = default);
    Task UpsertAsync(Venue venue, CancellationToken ct = default);
    Task DeleteAsync(string slug, CancellationToken ct = default);
}

public interface IClassRepository
{
    Task<IReadOnlyList<ClassSchedule>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ClassSchedule>> GetByVenueAsync(string venueSlug, CancellationToken ct = default);
    Task<ClassSchedule?> GetAsync(string venueSlug, string classId, CancellationToken ct = default);
    Task UpsertAsync(ClassSchedule schedule, CancellationToken ct = default);
    Task DeleteAsync(string venueSlug, string classId, CancellationToken ct = default);
}

public interface IUserRepository
{
    Task<User?> GetAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);
    /// <summary>Inserts the user; returns false if a user with that email already exists.</summary>
    Task<bool> TryAddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
}

public interface IMembershipRepository
{
    Task<Membership?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Membership>> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<Membership>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(Membership membership, CancellationToken ct = default);
}

public interface IBookingRepository
{
    /// <summary>Writes the booking to both the by-session and by-user views.</summary>
    Task UpsertAsync(Booking booking, CancellationToken ct = default);
    Task<Booking?> GetAsync(string classId, DateOnly date, string bookingId, CancellationToken ct = default);
    Task<IReadOnlyList<Booking>> GetForSessionAsync(string classId, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<Booking>> GetByEmailAsync(string email, CancellationToken ct = default);
}

public interface IEnquiryRepository
{
    Task AddAsync(Enquiry enquiry, CancellationToken ct = default);
    Task<IReadOnlyList<Enquiry>> GetSinceAsync(DateTimeOffset since, CancellationToken ct = default);
}

public interface IAccountTokenRepository
{
    Task AddAsync(AccountToken token, CancellationToken ct = default);
    Task<AccountToken?> GetAsync(string tokenHash, CancellationToken ct = default);
    Task UpdateAsync(AccountToken token, CancellationToken ct = default);
}

/// <summary>Records processed gateway webhook events so replays are ignored.</summary>
public interface IPaymentEventLog
{
    /// <summary>Atomically claims the event. Returns false if it was already claimed.</summary>
    Task<bool> TryClaimAsync(string eventId, string type, CancellationToken ct = default);
    /// <summary>Releases a claim after a processing failure so the gateway's retry can succeed.</summary>
    Task ReleaseAsync(string eventId, CancellationToken ct = default);
}

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string? ReplyTo = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed record AuthUser(string Email, string Name, UserRole Role);

public interface ITokenService
{
    string Issue(User user);
    AuthUser? Validate(string token);
    TimeSpan Lifetime { get; }
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public interface ICaptchaVerifier
{
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default);
}
