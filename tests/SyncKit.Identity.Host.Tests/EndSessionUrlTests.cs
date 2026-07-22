using Xunit;

namespace SyncKit.Identity.Host.Tests;

public class EndSessionUrlTests {
    [Fact]
    public void BuildEndSessionUrl_WithHintAndReturn_AppendsBoth() {
        var url = Program.BuildEndSessionUrl("https://auth.example.com/end", "id.tok.en", "https://app.example.com/");

        Assert.Equal("https://auth.example.com/end?id_token_hint=id.tok.en&post_logout_redirect_uri=https%3A%2F%2Fapp.example.com%2F", url);
    }

    [Fact]
    public void BuildEndSessionUrl_NoHint_OmitsHintParam() {
        var url = Program.BuildEndSessionUrl("https://auth.example.com/end", null, "https://app.example.com/");

        Assert.Equal("https://auth.example.com/end?post_logout_redirect_uri=https%3A%2F%2Fapp.example.com%2F", url);
    }

    [Fact]
    public void BuildEndSessionUrl_NoReturn_OmitsRedirectParam() {
        var url = Program.BuildEndSessionUrl("https://auth.example.com/end", "id.tok.en", null);

        Assert.Equal("https://auth.example.com/end?id_token_hint=id.tok.en", url);
    }

    [Fact]
    public void BuildEndSessionUrl_ExistingQuery_UsesAmpersand() {
        var url = Program.BuildEndSessionUrl("https://auth.example.com/end?foo=bar", null, "https://app.example.com/");

        Assert.Equal("https://auth.example.com/end?foo=bar&post_logout_redirect_uri=https%3A%2F%2Fapp.example.com%2F", url);
    }
}
