using Synckit.Auth;
using Xunit;

namespace Synckit.Auth.Tests;

public class DiscordOAuthTests
{
    // Go randomHex(n) returns 2n hex chars. GenerateEncryptionKey is randomHex(32) => 64 chars.
    [Fact]
    public void GenerateEncryptionKey_Is64HexChars()
    {
        var key = DiscordOAuth.GenerateEncryptionKey();
        Assert.Equal(64, key.Length);
        Assert.Matches("^[0-9a-f]{64}$", key);
    }

    [Fact]
    public void GenerateEncryptionKey_IsRandom()
    {
        Assert.NotEqual(DiscordOAuth.GenerateEncryptionKey(), DiscordOAuth.GenerateEncryptionKey());
    }

    [Fact]
    public void AuthUrl_ContainsStateAndClientId()
    {
        DiscordOAuth.Init("client123", "secret", "https://x/cb");
        var (url, state) = DiscordOAuth.AuthUrl();
        Assert.Equal(32, state.Length); // randomHex(16) => 32 chars
        Assert.Contains("client_id=client123", url);
        Assert.Contains("state=" + state, url);
        Assert.Contains("scope=identify", url);
        Assert.Contains("response_type=code", url);
    }
}
