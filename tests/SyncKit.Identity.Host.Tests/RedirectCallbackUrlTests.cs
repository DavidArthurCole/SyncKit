using Xunit;

namespace SyncKit.Identity.Host.Tests;

public class RedirectCallbackUrlTests {
    [Fact]
    public void BuildRedirectCallbackUrl_WithCode_AppendsCodeQueryParam() {
        var url = Program.BuildRedirectCallbackUrl("https://app.example.com", code: "abc123", error: null);

        Assert.Equal("https://app.example.com/auth/callback?code=abc123", url);
    }

    [Fact]
    public void BuildRedirectCallbackUrl_WithError_AppendsErrorQueryParam() {
        var url = Program.BuildRedirectCallbackUrl("https://app.example.com", code: null, error: "login_failed");

        Assert.Equal("https://app.example.com/auth/callback?error=login_failed", url);
    }

    [Fact]
    public void BuildRedirectCallbackUrl_EscapesCodeValue() {
        var url = Program.BuildRedirectCallbackUrl("https://app.example.com", code: "a b&c", error: null);

        Assert.Equal("https://app.example.com/auth/callback?code=a%20b%26c", url);
    }
}
