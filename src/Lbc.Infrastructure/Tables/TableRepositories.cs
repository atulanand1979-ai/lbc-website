using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Lbc.Core.Abstractions;
using Lbc.Core.Domain;

namespace Lbc.Infrastructure.Tables;

/// <summary>Creates table clients and makes sure the tables exist (once per process).</summary>
public sealed class TableStore(TableServiceClient service)
{
    public const string Venues = "Venues", Classes = "Classes", Users = "Users", Memberships = "Memberships",
        BookingsBySession = "BookingsBySession", BookingsByUser = "BookingsByUser", Enquiries = "Enquiries",
        AccountTokens = "AccountTokens", PaymentEvents = "PaymentEvents";

    public static readonly string[] All = [Venues, Classes, Users, Memberships, BookingsBySession, BookingsByUser, Enquiries, AccountTokens, PaymentEvents];

    private readonly Lazy<Task> _init = new(async () =>
    {
        foreach (var t in All) await service.GetTableClient(t).CreateIfNotExistsAsync();
    });

    public async Task<TableClient> GetAsync(string table)
    {
        await _init.Value;
        return service.GetTableClient(table);
    }

    internal static async Task<List<TableEntity>> QueryAsync(TableClient client, string? filter, CancellationToken ct)
    {
        var list = new List<TableEntity>();
        await foreach (var e in client.QueryAsync<TableEntity>(filter, cancellationToken: ct)) list.Add(e);
        return list;
    }

    internal static async Task<TableEntity?> GetOrNullAsync(TableClient client, string pk, string rk, CancellationToken ct)
    {
        var r = await client.GetEntityIfExistsAsync<TableEntity>(pk, rk, cancellationToken: ct);
        return r.HasValue ? r.Value : null;
    }

    /// <summary>Table keys may not contain / \ # ? or control characters.</summary>
    internal static string Key(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Any(c => c is '/' or '\\' or '#' or '?' || char.IsControl(c)))
            throw new ArgumentException($"Invalid table key '{value}'.");
        return value;
    }
}

internal static class EntityExt
{
    public static string S(this TableEntity e, string key) => e.GetString(key) ?? "";
    public static T Enum<T>(this TableEntity e, string key) where T : struct, System.Enum =>
        System.Enum.TryParse<T>(e.GetString(key), out var v) ? v : default;
    public static DateOnly? Date(this TableEntity e, string key) =>
        DateOnly.TryParseExact(e.GetString(key) ?? "", "yyyy-MM-dd", out var d) ? d : null;
    public static string? Iso(DateOnly? d) => d?.ToString("yyyy-MM-dd");
}

public sealed class VenueRepository(TableStore store) : IVenueRepository
{
    private const string Pk = "venue";

    public async Task<IReadOnlyList<Venue>> GetAllAsync(CancellationToken ct = default) =>
        (await TableStore.QueryAsync(await store.GetAsync(TableStore.Venues), $"PartitionKey eq '{Pk}'", ct)).Select(Map).ToList();

    public async Task<Venue?> GetAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Any(c => c is '/' or '\\' or '#' or '?')) return null;
        var e = await TableStore.GetOrNullAsync(await store.GetAsync(TableStore.Venues), Pk, slug, ct);
        return e is null ? null : Map(e);
    }

    public async Task UpsertAsync(Venue v, CancellationToken ct = default) =>
        await (await store.GetAsync(TableStore.Venues)).UpsertEntityAsync(new TableEntity(Pk, TableStore.Key(v.Slug))
        {
            ["City"] = v.City, ["Name"] = v.Name, ["Address"] = v.Address, ["Postcode"] = v.Postcode,
            ["MapUrl"] = v.MapUrl, ["IsActive"] = v.IsActive, ["StartsOn"] = EntityExt.Iso(v.StartsOn), ["SortOrder"] = v.SortOrder,
        }, TableUpdateMode.Replace, ct);

    public async Task DeleteAsync(string slug, CancellationToken ct = default) =>
        await (await store.GetAsync(TableStore.Venues)).DeleteEntityAsync(Pk, TableStore.Key(slug), ETag.All, ct);

    private static Venue Map(TableEntity e) => new()
    {
        Slug = e.RowKey, City = e.S("City"), Name = e.S("Name"), Address = e.S("Address"), Postcode = e.S("Postcode"),
        MapUrl = e.GetString("MapUrl"), IsActive = e.GetBoolean("IsActive") ?? true, StartsOn = e.Date("StartsOn"),
        SortOrder = e.GetInt32("SortOrder") ?? 0,
    };
}

public sealed class ClassRepository(TableStore store) : IClassRepository
{
    public async Task<IReadOnlyList<ClassSchedule>> GetAllAsync(CancellationToken ct = default) =>
        (await TableStore.QueryAsync(await store.GetAsync(TableStore.Classes), null, ct)).Select(Map).ToList();

    public async Task<IReadOnlyList<ClassSchedule>> GetByVenueAsync(string venueSlug, CancellationToken ct = default) =>
        (await TableStore.QueryAsync(await store.GetAsync(TableStore.Classes),
            TableClient.CreateQueryFilter($"PartitionKey eq {venueSlug}"), ct)).Select(Map).ToList();

    public async Task<ClassSchedule?> GetAsync(string venueSlug, string classId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(venueSlug) || string.IsNullOrWhiteSpace(classId)) return null;
        if ((venueSlug + classId).Any(c => c is '/' or '\\' or '#' or '?')) return null;
        var e = await TableStore.GetOrNullAsync(await store.GetAsync(TableStore.Classes), venueSlug, classId, ct);
        return e is null ? null : Map(e);
    }

    public async Task UpsertAsync(ClassSchedule c, CancellationToken ct = default) =>
        await (await store.GetAsync(TableStore.Classes)).UpsertEntityAsync(new TableEntity(TableStore.Key(c.VenueSlug), TableStore.Key(c.Id))
        {
            ["DayOfWeek"] = c.DayOfWeek.ToString(), ["StartTime"] = c.StartTime.ToString("HH:mm"), ["EndTime"] = c.EndTime.ToString("HH:mm"),
            ["Level"] = c.Level.ToString(), ["IsActive"] = c.IsActive,
            ["SkipDates"] = JsonSerializer.Serialize(c.SkipDates.Select(d => d.ToString("yyyy-MM-dd"))),
        }, TableUpdateMode.Replace, ct);

    public async Task DeleteAsync(string venueSlug, string classId, CancellationToken ct = default) =>
        await (await store.GetAsync(TableStore.Classes)).DeleteEntityAsync(TableStore.Key(venueSlug), TableStore.Key(classId), ETag.All, ct);

    private static ClassSchedule Map(TableEntity e) => new()
    {
        Id = e.RowKey, VenueSlug = e.PartitionKey, DayOfWeek = e.Enum<DayOfWeek>("DayOfWeek"),
        StartTime = TimeOnly.ParseExact(e.S("StartTime"), "HH:mm"), EndTime = TimeOnly.ParseExact(e.S("EndTime"), "HH:mm"),
        Level = e.Enum<ClassLevel>("Level"), IsActive = e.GetBoolean("IsActive") ?? true,
        SkipDates = (JsonSerializer.Deserialize<List<string>>(e.GetString("SkipDates") ?? "[]") ?? [])
            .Select(s => DateOnly.ParseExact(s, "yyyy-MM-dd")).ToList(),
    };
}

public sealed class UserRepository(TableStore store) : IUserRepository
{
    private const string Rk = "profile";

    public async Task<User?> GetAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Any(c => c is '/' or '\\' or '#' or '?' || char.IsControl(c))) return null;
        var e = await TableStore.GetOrNullAsync(await store.GetAsync(TableStore.Users), email, Rk, ct);
        return e is null ? null : Map(e);
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) =>
        (await TableStore.QueryAsync(await store.GetAsync(TableStore.Users), $"RowKey eq '{Rk}'", ct)).Select(Map).ToList();

    public async Task<bool> TryAddAsync(User u, CancellationToken ct = default)
    {
        try
        {
            await (await store.GetAsync(TableStore.Users)).AddEntityAsync(ToEntity(u), ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409) { return false; }
    }

    public async Task UpdateAsync(User u, CancellationToken ct = default) =>
        await (await store.GetAsync(TableStore.Users)).UpsertEntityAsync(ToEntity(u), TableUpdateMode.Replace, ct);

    private static TableEntity ToEntity(User u) => new(TableStore.Key(u.Email), Rk)
    {
        ["UserId"] = u.UserId, ["Name"] = u.Name, ["Phone"] = u.Phone, ["PasswordHash"] = u.PasswordHash,
        ["Role"] = u.Role.ToString(), ["CreatedAt"] = u.CreatedAt,
    };

    private static User Map(TableEntity e) => new()
    {
        Email = e.PartitionKey, UserId = e.S("UserId"), Name = e.S("Name"), Phone = e.S("Phone"),
        PasswordHash = e.GetString("PasswordHash"), Role = e.Enum<UserRole>("Role"),
        CreatedAt = e.GetDateTimeOffset("CreatedAt") ?? default,
    };
}

/// <summary>Memberships live in one partition: the table stays small (one row per member), so scans are cheap.</summary>
public sealed class MembershipRepository(TableStore store) : IMembershipRepository
{
    private const string Pk = "membership";

    public async Task<Membership?> GetAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(c => c is '/' or '\\' or '#' or '?')) return null;
        var e = await TableStore.GetOrNullAsync(await store.GetAsync(TableStore.Memberships), Pk, id, ct);
        return e is null ? null : Map(e);
    }

    public async Task<IReadOnlyList<Membership>> GetByEmailAsync(string email, CancellationToken ct = default) =>
        (await TableStore.QueryAsync(await store.GetAsync(TableStore.Memberships),
            TableClient.CreateQueryFilter($"PartitionKey eq {Pk} and Email eq {email}"), ct)).Select(Map).ToList();

    public async Task<IReadOnlyList<Membership>> GetAllAsync(CancellationToken ct = default) =>
        (await TableStore.QueryAsync(await store.GetAsync(TableStore.Memberships), $"PartitionKey eq '{Pk}'", ct)).Select(Map).ToList();

    public async Task UpsertAsync(Membership m, CancellationToken ct = default) =>
        await (await store.GetAsync(TableStore.Memberships)).UpsertEntityAsync(new TableEntity(Pk, TableStore.Key(m.Id))
        {
            ["Email"] = m.Email, ["Tier"] = m.Tier.ToString(), ["HomeVenueSlug"] = m.HomeVenueSlug, ["Status"] = m.Status.ToString(),
            ["MonthlyPricePence"] = m.MonthlyPricePence, ["GatewayRecurringRef"] = m.GatewayRecurringRef,
            ["CurrentPeriodEnd"] = m.CurrentPeriodEnd, ["CreatedAt"] = m.CreatedAt, ["CancelledAt"] = m.CancelledAt,
        }, TableUpdateMode.Replace, ct);

    private static Membership Map(TableEntity e) => new()
    {
        Id = e.RowKey, Email = e.S("Email"), Tier = e.Enum<MembershipTier>("Tier"), HomeVenueSlug = e.GetString("HomeVenueSlug"),
        Status = e.Enum<MembershipStatus>("Status"), MonthlyPricePence = e.GetInt32("MonthlyPricePence") ?? 0,
        GatewayRecurringRef = e.GetString("GatewayRecurringRef"), CurrentPeriodEnd = e.GetDateTimeOffset("CurrentPeriodEnd"),
        CreatedAt = e.GetDateTimeOffset("CreatedAt") ?? default, CancelledAt = e.GetDateTimeOffset("CancelledAt"),
    };
}

/// <summary>Each booking is stored twice: by session (class register) and by user (account page).</summary>
public sealed class BookingRepository(TableStore store) : IBookingRepository
{
    private static string SessionPk(string classId, DateOnly date) => TableStore.Key($"{classId}_{date:yyyyMMdd}");
    private static string UserRk(Booking b) => $"{b.Date:yyyyMMdd}_{b.Id}";

    public async Task UpsertAsync(Booking b, CancellationToken ct = default)
    {
        await (await store.GetAsync(TableStore.BookingsBySession)).UpsertEntityAsync(ToEntity(b, SessionPk(b.ClassId, b.Date), TableStore.Key(b.Id)), TableUpdateMode.Replace, ct);
        await (await store.GetAsync(TableStore.BookingsByUser)).UpsertEntityAsync(ToEntity(b, TableStore.Key(b.Email), UserRk(b)), TableUpdateMode.Replace, ct);
    }

    public async Task<Booking?> GetAsync(string classId, DateOnly date, string bookingId, CancellationToken ct = default)
    {
        var e = await TableStore.GetOrNullAsync(await store.GetAsync(TableStore.BookingsBySession), SessionPk(classId, date), TableStore.Key(bookingId), ct);
        return e is null ? null : Map(e);
    }

    public async Task<IReadOnlyList<Booking>> GetForSessionAsync(string classId, DateOnly date, CancellationToken ct = default) =>
        (await TableStore.QueryAsync(await store.GetAsync(TableStore.BookingsBySession),
            TableClient.CreateQueryFilter($"PartitionKey eq {SessionPk(classId, date)}"), ct)).Select(Map).ToList();

    public async Task<IReadOnlyList<Booking>> GetByEmailAsync(string email, CancellationToken ct = default) =>
        (await TableStore.QueryAsync(await store.GetAsync(TableStore.BookingsByUser),
            TableClient.CreateQueryFilter($"PartitionKey eq {email}"), ct)).Select(Map).ToList();

    private static TableEntity ToEntity(Booking b, string pk, string rk) => new(pk, rk)
    {
        ["BookingId"] = b.Id, ["VenueSlug"] = b.VenueSlug, ["ClassId"] = b.ClassId, ["Date"] = b.Date.ToString("yyyy-MM-dd"),
        ["Email"] = b.Email, ["Name"] = b.Name, ["Phone"] = b.Phone, ["Status"] = b.Status.ToString(),
        ["AmountPence"] = b.AmountPence, ["ClassLabel"] = b.ClassLabel, ["GatewayPaymentRef"] = b.GatewayPaymentRef,
        ["CreatedAt"] = b.CreatedAt, ["PaidAt"] = b.PaidAt,
    };

    private static Booking Map(TableEntity e) => new()
    {
        Id = e.S("BookingId"), VenueSlug = e.S("VenueSlug"), ClassId = e.S("ClassId"), Date = e.Date("Date") ?? default,
        Email = e.S("Email"), Name = e.S("Name"), Phone = e.S("Phone"), Status = e.Enum<BookingStatus>("Status"),
        AmountPence = e.GetInt32("AmountPence") ?? 0, ClassLabel = e.S("ClassLabel"), GatewayPaymentRef = e.GetString("GatewayPaymentRef"),
        CreatedAt = e.GetDateTimeOffset("CreatedAt") ?? default, PaidAt = e.GetDateTimeOffset("PaidAt"),
    };
}

/// <summary>Partitioned by month (yyyyMM) so "recent enquiries" only reads a few partitions.</summary>
public sealed class EnquiryRepository(TableStore store) : IEnquiryRepository
{
    public async Task AddAsync(Enquiry q, CancellationToken ct = default) =>
        await (await store.GetAsync(TableStore.Enquiries)).AddEntityAsync(new TableEntity(q.CreatedAt.ToString("yyyyMM"), TableStore.Key(q.Id))
        {
            ["Type"] = q.Type.ToString(), ["Name"] = q.Name, ["Email"] = q.Email, ["Phone"] = q.Phone, ["Topic"] = q.Topic,
            ["EventDate"] = EntityExt.Iso(q.EventDate), ["Message"] = q.Message, ["CreatedAt"] = q.CreatedAt,
        }, ct);

    public async Task<IReadOnlyList<Enquiry>> GetSinceAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        var filter = TableClient.CreateQueryFilter($"PartitionKey ge {since.ToString("yyyyMM")} and CreatedAt ge {since}");
        return (await TableStore.QueryAsync(await store.GetAsync(TableStore.Enquiries), filter, ct)).Select(e => new Enquiry
        {
            Id = e.RowKey, Type = e.Enum<EnquiryType>("Type"), Name = e.S("Name"), Email = e.S("Email"), Phone = e.S("Phone"),
            Topic = e.S("Topic"), EventDate = e.Date("EventDate"), Message = e.S("Message"), CreatedAt = e.GetDateTimeOffset("CreatedAt") ?? default,
        }).ToList();
    }
}

public sealed class AccountTokenRepository(TableStore store) : IAccountTokenRepository
{
    private const string Rk = "token";

    public async Task AddAsync(AccountToken t, CancellationToken ct = default) =>
        await (await store.GetAsync(TableStore.AccountTokens)).AddEntityAsync(ToEntity(t), ct);

    public async Task<AccountToken?> GetAsync(string tokenHash, CancellationToken ct = default)
    {
        var e = await TableStore.GetOrNullAsync(await store.GetAsync(TableStore.AccountTokens), TableStore.Key(tokenHash), Rk, ct);
        return e is null ? null : new AccountToken
        {
            TokenHash = e.PartitionKey, Email = e.S("Email"), Purpose = e.Enum<AccountTokenPurpose>("Purpose"),
            ExpiresAt = e.GetDateTimeOffset("ExpiresAt") ?? default, UsedAt = e.GetDateTimeOffset("UsedAt"),
        };
    }

    public async Task UpdateAsync(AccountToken t, CancellationToken ct = default) =>
        await (await store.GetAsync(TableStore.AccountTokens)).UpsertEntityAsync(ToEntity(t), TableUpdateMode.Replace, ct);

    private static TableEntity ToEntity(AccountToken t) => new(TableStore.Key(t.TokenHash), Rk)
    {
        ["Email"] = t.Email, ["Purpose"] = t.Purpose.ToString(), ["ExpiresAt"] = t.ExpiresAt, ["UsedAt"] = t.UsedAt,
    };
}

public sealed class PaymentEventLog(TableStore store) : IPaymentEventLog
{
    private const string Rk = "event";

    private static string Pk(string eventId) =>
        // Gateway ids are usually key-safe; hash anything that isn't.
        eventId.Length <= 200 && !eventId.Any(c => c is '/' or '\\' or '#' or '?' || char.IsControl(c))
            ? eventId : Lbc.Core.Services.Ids.Sha256(eventId);

    public async Task<bool> TryClaimAsync(string eventId, string type, CancellationToken ct = default)
    {
        try
        {
            await (await store.GetAsync(TableStore.PaymentEvents)).AddEntityAsync(
                new TableEntity(Pk(eventId), Rk) { ["Type"] = type, ["EventId"] = eventId, ["ClaimedAt"] = DateTimeOffset.UtcNow }, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409) { return false; }
    }

    public async Task ReleaseAsync(string eventId, CancellationToken ct = default) =>
        await (await store.GetAsync(TableStore.PaymentEvents)).DeleteEntityAsync(Pk(eventId), Rk, ETag.All, ct);
}
