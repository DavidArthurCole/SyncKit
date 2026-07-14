using SyncKit.Auth;
using Xunit;

namespace SyncKit.Auth.Tests;

public class AuthentikOAuthTests {
    [Fact]
    public void AuthUrl_ContainsPkceAndClientId() {
        AuthentikOAuth.Init("https://auth.example.com/application/o/synckit-login/", "client123", "secret", "https://identity.example.com/login/callback");
        var (url, state, verifier) = AuthentikOAuth.AuthUrl();

        Assert.Contains("client_id=client123", url);
        Assert.Contains("state=" + state, url);
        Assert.Contains("code_challenge=", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("scope=openid+profile+email+discord_id", url);
        Assert.True(verifier.Length >= 43); // RFC 7636 minimum verifier length
    }

    [Fact]
    public void AuthUrl_StateAndVerifierAreRandomPerCall() {
        AuthentikOAuth.Init("https://auth.example.com/application/o/synckit-login/", "client123", "secret", "https://identity.example.com/login/callback");
        var (_, state1, verifier1) = AuthentikOAuth.AuthUrl();
        var (_, state2, verifier2) = AuthentikOAuth.AuthUrl();

        Assert.NotEqual(state1, state2);
        Assert.NotEqual(verifier1, verifier2);
    }

    [Fact]
    public void BuildAuthParams_ContainsSameParamsAsAuthUrl_WithoutAuthorizeEndpointPrefix() {
        AuthentikOAuth.Init("https://auth.example.com", "client123", "secret", "https://identity.example.com/login/callback");
        var (query, state, verifier) = AuthentikOAuth.BuildAuthParams();

        Assert.DoesNotContain("/application/o/authorize/", query);
        Assert.DoesNotContain("?", query); // raw query params only, no leading '?'
        Assert.Contains("client_id=client123", query);
        Assert.Contains("state=" + state, query);
        Assert.Contains("code_challenge=", query);
        Assert.Contains("code_challenge_method=S256", query);
        Assert.Contains("response_type=code", query);
        Assert.Contains("scope=openid+profile+email+discord_id", query);
        Assert.True(verifier.Length >= 43);
    }

    [Fact]
    public void AuthUrl_StillWorks_AfterRefactor() {
        AuthentikOAuth.Init("https://auth.example.com", "client123", "secret", "https://identity.example.com/login/callback");
        var (url, state, verifier) = AuthentikOAuth.AuthUrl();

        Assert.StartsWith("https://auth.example.com/application/o/authorize/?", url);
        Assert.Contains("client_id=client123", url);
        Assert.Contains("state=" + state, url);
        Assert.True(verifier.Length >= 43);
    }

    [Fact]
    public void CodeChallenge_IsS256OfVerifier() {
        // RFC 7636 Appendix B test vector.
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var challenge = AuthentikOAuth.ComputeCodeChallenge(verifier);

        Assert.Equal(expectedChallenge, challenge);
    }
}
