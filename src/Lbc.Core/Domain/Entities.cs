namespace Lbc.Core.Domain;

/// <summary>A place where classes run, e.g. Leicester – Avanti Fields School.</summary>
public sealed class Venue
{
    /// <summary>URL-safe key, e.g. "leicester". Immutable once created.</summary>
    public required string Slug { get; init; }
    public required string City { get; set; }
    public required string Name { get; set; }
    public string Address { get; set; } = "";
    public string Postcode { get; set; } = "";
    public string? MapUrl { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>First date classes run here (e.g. a new venue launching in February). Null = already running.</summary>
    public DateOnly? StartsOn { get; set; }
    public int SortOrder { get; set; }
}

public enum ClassLevel { Beginners, Intermediate }

/// <summary>A class that repeats every week at a venue, e.g. every Tuesday 19:00–20:00 Beginners.</summary>
public sealed class ClassSchedule
{
    public required string Id { get; init; }
    public required string VenueSlug { get; init; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public ClassLevel Level { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Dates this class does not run (bank holidays, venue closed).</summary>
    public List<DateOnly> SkipDates { get; set; } = [];
}

public enum UserRole { Member, Admin }

public sealed class User
{
    /// <summary>Lower-cased, trimmed email. Identifies the user.</summary>
    public required string Email { get; init; }
    public required string UserId { get; init; }
    public required string Name { get; set; }
    public string Phone { get; set; } = "";
    /// <summary>Null until the user follows the set-password link.</summary>
    public string? PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Member;
    public DateTimeOffset CreatedAt { get; init; }
    public bool HasPassword => !string.IsNullOrEmpty(PasswordHash);
}

public enum MembershipTier { Bronze, Silver, Gold }

public enum MembershipStatus { Pending, Active, PastDue, Cancelled }

public sealed class Membership
{
    public required string Id { get; init; }
    public required string Email { get; init; }
    public MembershipTier Tier { get; set; }
    /// <summary>Only used for Bronze (unlimited classes within one city).</summary>
    public string? HomeVenueSlug { get; set; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Pending;
    public int MonthlyPricePence { get; set; }
    /// <summary>The gateway's id for the recurring payment (subscription / mandate). Set once started.</summary>
    public string? GatewayRecurringRef { get; set; }
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CancelledAt { get; set; }

    public bool IsLive => Status is MembershipStatus.Active or MembershipStatus.PastDue;
}

public enum BookingStatus { Pending, Paid }

/// <summary>A Pay As You Go booking for one class on one date.</summary>
public sealed class Booking
{
    public required string Id { get; init; }
    public required string VenueSlug { get; init; }
    public required string ClassId { get; init; }
    public required DateOnly Date { get; init; }
    public required string Email { get; init; }
    public required string Name { get; set; }
    public string Phone { get; set; } = "";
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public int AmountPence { get; set; }
    /// <summary>Denormalised for display so history survives class edits.</summary>
    public string ClassLabel { get; set; } = "";
    public string? GatewayPaymentRef { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? PaidAt { get; set; }
}

public enum EnquiryType { Contact, Workshop, BookUs, OneToOne, PrivateGroup }

public sealed class Enquiry
{
    public required string Id { get; init; }
    public EnquiryType Type { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public string Phone { get; init; } = "";
    /// <summary>Free-form subject, e.g. "Corporate workshop" or "Weddings".</summary>
    public string Topic { get; init; } = "";
    public DateOnly? EventDate { get; init; }
    public string Message { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
}

public enum AccountTokenPurpose { SetPassword, ResetPassword }

public sealed class AccountToken
{
    /// <summary>SHA-256 of the raw token; the raw token only ever exists in the emailed link.</summary>
    public required string TokenHash { get; init; }
    public required string Email { get; init; }
    public AccountTokenPurpose Purpose { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? UsedAt { get; set; }
}
