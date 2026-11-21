using Lbc.Core.Domain;
using Lbc.Infrastructure;
using Microsoft.Extensions.Options;

namespace Lbc.Tests;

public class SecurityTests
{
    private static JwtTokenService Jwt(FakeClock clock, string key = "test-signing-key-0123456789-abcdefghij") =>
        new(Options.Create(new JwtOptions { SigningKey = key, LifetimeDays = 7 }), clock);

    private static readonly User Admin = new() { Email = "a@example.com", UserId = "u1", Name = "Aman", Role = UserRole.Admin };

    [Fact]
    public void Jwt_round_trips_role()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var jwt = Jwt(clock);
        var user = jwt.Validate(jwt.Issue(Admin));
        Assert.NotNull(user);
        Assert.Equal(("a@example.com", UserRole.Admin), (user!.Email, user.Role));
    }

    [Fact]
    public void Jwt_rejects_expired_tampered_and_foreign_tokens()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var jwt = Jwt(clock);
        var token = jwt.Issue(Admin);

        Assert.Null(Jwt(clock, "another-signing-key-0123456789-abcdefgh").Validate(token));
        var parts = token.Split('.');
        Assert.Null(jwt.Validate($"{parts[0]}.{parts[1]}x.{parts[2]}"));

        clock.Now = clock.Now.AddDays(8);
        Assert.Null(jwt.Validate(token));
    }

    [Fact]
    public void Short_signing_key_is_refused() =>
        Assert.Throws<InvalidOperationException>(() => Jwt(new FakeClock(DateTimeOffset.UtcNow), "short"));

    [Fact]
    public void Password_hash_verifies()
    {
        var p = new PasswordService();
        var hash = p.Hash("correct horse battery");
        Assert.True(p.Verify(hash, "correct horse battery"));
        Assert.False(p.Verify(hash, "wrong"));
    }
}
