using Xunit;

namespace SyncKit.Identity.Host.Tests;

public class RelinkUrlTests {
    private static readonly string[] Providers = ["discord", "google", "microsoft", "github"];

    [Fact]
    public void BuildRelinkContinueUrl_CallbackSuffix_ReplacesWithRelinkContinue() {
        var url = Program.BuildRelinkContinueUrl("https://id.example.com/login/callback");

        Assert.Equal("https://id.example.com/login/relink/continue", url);
    }

    [Fact]
    public void BuildRelinkContinueUrl_NoCallbackSuffix_AppendsRelinkContinue() {
        var url = Program.BuildRelinkContinueUrl("https://id.example.com/login/");

        Assert.Equal("https://id.example.com/login/relink/continue", url);
    }

    [Fact]
    public void BuildRelinkLogoutUrl_AppendsParamsInOrderEscaped() {
        var url = Program.BuildRelinkLogoutUrl(
            "https://auth.example.com/end",
            "id.tok.en",
            "https://id.example.com/login/relink/continue",
            "https://auth.example.com/if/flow/discord-only-auth/?next=x");

        Assert.Equal(
            "https://auth.example.com/end?id_token_hint=id.tok.en"
            + "&post_logout_redirect_uri=https%3A%2F%2Fid.example.com%2Flogin%2Frelink%2Fcontinue"
            + "&state=https%3A%2F%2Fauth.example.com%2Fif%2Fflow%2Fdiscord-only-auth%2F%3Fnext%3Dx",
            url);
    }

    [Fact]
    public void BuildRelinkLogoutUrl_ExistingQuery_UsesAmpersand() {
        var url = Program.BuildRelinkLogoutUrl(
            "https://auth.example.com/end?foo=bar",
            "id.tok.en",
            "https://id.example.com/login/relink/continue",
            "https://auth.example.com/if/flow/discord-only-auth/?next=x");

        Assert.StartsWith("https://auth.example.com/end?foo=bar&id_token_hint=id.tok.en", url);
    }

    [Fact]
    public void IsAllowedRelinkTarget_KnownProviderFlowUrl_ReturnsTrue() {
        var allowed = Program.IsAllowedRelinkTarget(
            "https://auth.example.com/if/flow/discord-only-auth/?next=https%3A%2F%2Fauth.example.com%2Fapplication%2Fo%2Fauthorize%2F",
            "https://auth.example.com", Providers);

        Assert.True(allowed);
    }

    [Fact]
    public void IsAllowedRelinkTarget_Null_ReturnsFalse() {
        Assert.False(Program.IsAllowedRelinkTarget(null, "https://auth.example.com", Providers));
    }

    [Fact]
    public void IsAllowedRelinkTarget_Empty_ReturnsFalse() {
        Assert.False(Program.IsAllowedRelinkTarget("", "https://auth.example.com", Providers));
    }

    [Fact]
    public void IsAllowedRelinkTarget_RelativeUrl_ReturnsFalse() {
        Assert.False(Program.IsAllowedRelinkTarget("/if/flow/discord-only-auth/", "https://auth.example.com", Providers));
    }

    [Fact]
    public void IsAllowedRelinkTarget_WrongAuthority_ReturnsFalse() {
        Assert.False(Program.IsAllowedRelinkTarget("https://evil.com/if/flow/discord-only-auth/", "https://auth.example.com", Providers));
    }

    [Fact]
    public void IsAllowedRelinkTarget_AuthorityPrefixTrick_ReturnsFalse() {
        Assert.False(Program.IsAllowedRelinkTarget("https://auth.example.com.evil.com/if/flow/discord-only-auth/", "https://auth.example.com", Providers));
    }

    [Fact]
    public void IsAllowedRelinkTarget_UnknownFlowSlug_ReturnsFalse() {
        Assert.False(Program.IsAllowedRelinkTarget("https://auth.example.com/if/flow/other-auth/", "https://auth.example.com", Providers));
    }

    [Fact]
    public void IsAllowedRelinkTarget_NonFlowPath_ReturnsFalse() {
        Assert.False(Program.IsAllowedRelinkTarget("https://auth.example.com/foo", "https://auth.example.com", Providers));
    }

    [Fact]
    public void IsAllowedRelinkTarget_ForeignNextParam_ReturnsFalse() {
        Assert.False(Program.IsAllowedRelinkTarget(
            "https://auth.example.com/if/flow/discord-only-auth/?next=https%3A%2F%2Fevil.com%2Fphish",
            "https://auth.example.com", Providers));
    }

    [Fact]
    public void IsAllowedRelinkTarget_MissingNextParam_ReturnsFalse() {
        Assert.False(Program.IsAllowedRelinkTarget(
            "https://auth.example.com/if/flow/discord-only-auth/",
            "https://auth.example.com", Providers));
    }
}
