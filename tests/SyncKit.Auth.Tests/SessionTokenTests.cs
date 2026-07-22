using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using SyncKit.Auth;
using Xunit;

namespace SyncKit.Auth.Tests;

public class SessionTokenTests {
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static SessionCookieOptions Options(string secret = "super-secret-signing-key-of-sufficient-length", string issuer = "eggidentity", string audience = "synckit-apps") =>
        new() { SigningSecret = secret, Issuer = issuer, Audience = audience, Ttl = TimeSpan.FromMinutes(480) };

    private static SessionUser User(string sid = "sess-123") =>
        new(UserId: "8d2f0e94-1c3a-4b6e-9f11-2a7c5d0e1234", Sid: sid, Role: "admin", Name: "alice", Avatar: "pic", DiscordId: "42");

    [Fact]
    public void Roundtrip_PreservesClaims() {
        var options = Options();
        var token = SessionToken.Issue(options, User(), Now);

        var principal = SessionToken.Validate(options, token, Now);

        Assert.NotNull(principal);
        Assert.Equal("8d2f0e94-1c3a-4b6e-9f11-2a7c5d0e1234", principal!.FindFirstValue(JwtRegisteredClaimNames.Sub));
        Assert.Equal("admin", principal.FindFirstValue(SessionClaims.Role));
        Assert.Equal("sess-123", principal.FindFirstValue(SessionClaims.SessionId));
        Assert.Equal("alice", principal.FindFirstValue(SessionClaims.Name));
        Assert.Equal("42", principal.FindFirstValue(SessionClaims.DiscordId));
    }

    [Fact]
    public void OmittedSid_ProducesNoSidClaim() {
        var options = Options();
        var token = SessionToken.Issue(options, User(sid: ""), Now);

        var principal = SessionToken.Validate(options, token, Now);

        Assert.NotNull(principal);
        Assert.Null(principal!.FindFirstValue(SessionClaims.SessionId));
    }

    [Fact]
    public void TamperedToken_FailsValidation() {
        var options = Options();
        var token = SessionToken.Issue(options, User(), Now);
        var parts = token.Split('.');
        var lastChar = parts[2][^1] == 'a' ? 'b' : 'a';
        parts[2] = parts[2][..^1] + lastChar;
        var tampered = string.Join('.', parts);

        Assert.Null(SessionToken.Validate(options, tampered, Now));
    }

    [Fact]
    public void WrongSecret_FailsValidation() {
        var token = SessionToken.Issue(Options(secret: "one-secret-that-is-long-enough-aaaaaaaa"), User(), Now);

        Assert.Null(SessionToken.Validate(Options(secret: "another-secret-that-is-long-enough-bbbb"), token, Now));
    }

    [Fact]
    public void WrongIssuer_FailsValidation() {
        var token = SessionToken.Issue(Options(issuer: "eggidentity"), User(), Now);

        Assert.Null(SessionToken.Validate(Options(issuer: "someone-else"), token, Now));
    }

    [Fact]
    public void WrongAudience_FailsValidation() {
        var token = SessionToken.Issue(Options(audience: "synckit-apps"), User(), Now);

        Assert.Null(SessionToken.Validate(Options(audience: "other-apps"), token, Now));
    }

    [Fact]
    public void Expired_FailsValidation() {
        var options = Options();
        var token = SessionToken.Issue(options, User(), Now);

        var wellAfterExpiry = Now + options.Ttl + TimeSpan.FromMinutes(2);

        Assert.Null(SessionToken.Validate(options, token, wellAfterExpiry));
    }

    [Fact]
    public void JustPastExpiry_WithinSkew_StillValid() {
        var options = Options();
        var token = SessionToken.Issue(options, User(), Now);

        var justAfterExpiry = Now + options.Ttl + TimeSpan.FromSeconds(30);

        Assert.NotNull(SessionToken.Validate(options, token, justAfterExpiry));
    }
}
