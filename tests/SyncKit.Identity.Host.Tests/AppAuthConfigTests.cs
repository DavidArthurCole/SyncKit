using SyncKit.Identity.Host;
using Xunit;

namespace SyncKit.Identity.Host.Tests;

public class AppAuthConfigTests {
    private static string WriteTempDir(params (string fileName, string content)[] files) {
        var dir = Path.Combine(Path.GetTempPath(), "synckit-apps-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        foreach (var (fileName, content) in files)
            File.WriteAllText(Path.Combine(dir, fileName), content);
        return dir;
    }

    [Fact]
    public void LoadFromDirectory_SingleValidFile_ReturnsOneEntryKeyedByOrigin() {
        var dir = WriteTempDir(("egi.app.0", """
            Origin=https://egg-incognito.example.com
            ClientId=egi-client
            ClientSecret=egi-secret
            CallbackUrl=https://identity.example.com/login/callback
            """));

        var result = AppAuthConfigLoader.LoadFromDirectory(dir, "https://auth.example.com");

        Assert.Single(result);
        Assert.True(result.ContainsKey("https://egg-incognito.example.com"));
        Assert.Equal("https://egg-incognito.example.com", result["https://egg-incognito.example.com"].Origin);
    }

    [Fact]
    public void LoadFromDirectory_MultipleFiles_ReturnsOneEntryPerApp() {
        var dir = WriteTempDir(
            ("egi.app.0", """
                Origin=https://egg-incognito.example.com
                ClientId=egi-client
                ClientSecret=egi-secret
                CallbackUrl=https://identity.example.com/login/callback
                """),
            ("egl.app.1", """
                Origin=https://egg-ledger.example.com
                ClientId=egl-client
                ClientSecret=egl-secret
                CallbackUrl=https://identity.example.com/login/callback
                """));

        var result = AppAuthConfigLoader.LoadFromDirectory(dir, "https://auth.example.com");

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("https://egg-incognito.example.com"));
        Assert.True(result.ContainsKey("https://egg-ledger.example.com"));
    }

    [Fact]
    public void LoadFromDirectory_MissingRequiredKey_Throws() {
        var dir = WriteTempDir(("broken.app.0", """
            Origin=https://egg-incognito.example.com
            ClientId=egi-client
            """));

        Assert.Throws<InvalidOperationException>(() => AppAuthConfigLoader.LoadFromDirectory(dir, "https://auth.example.com"));
    }

    [Fact]
    public void LoadFromDirectory_EmptyDirectory_ReturnsEmptyDictionary() {
        var dir = WriteTempDir();

        var result = AppAuthConfigLoader.LoadFromDirectory(dir, "https://auth.example.com");

        Assert.Empty(result);
    }

    [Fact]
    public void LoadFromDirectory_WithEndSessionUrl_PopulatesEndSessionUrl() {
        var dir = WriteTempDir(("egi.app.0", """
            Origin=https://egg-incognito.example.com
            ClientId=egi-client
            ClientSecret=egi-secret
            CallbackUrl=https://identity.example.com/login/callback
            EndSessionUrl=https://auth.example.com/application/o/egi/end-session/
            """));

        var result = AppAuthConfigLoader.LoadFromDirectory(dir, "https://auth.example.com");

        Assert.Equal("https://auth.example.com/application/o/egi/end-session/", result["https://egg-incognito.example.com"].EndSessionUrl);
    }

    [Fact]
    public void LoadFromDirectory_WithoutEndSessionUrl_EndSessionUrlIsNull() {
        var dir = WriteTempDir(("egi.app.0", """
            Origin=https://egg-incognito.example.com
            ClientId=egi-client
            ClientSecret=egi-secret
            CallbackUrl=https://identity.example.com/login/callback
            """));

        var result = AppAuthConfigLoader.LoadFromDirectory(dir, "https://auth.example.com");

        Assert.Null(result["https://egg-incognito.example.com"].EndSessionUrl);
    }

    [Fact]
    public void LoadFromDirectory_BlankLinesAndWhitespace_AreIgnored() {
        var dir = WriteTempDir(("egi.app.0", """
            Origin=https://egg-incognito.example.com

            ClientId=egi-client
            ClientSecret=egi-secret
            CallbackUrl=https://identity.example.com/login/callback

            """));

        var result = AppAuthConfigLoader.LoadFromDirectory(dir, "https://auth.example.com");

        Assert.Single(result);
    }
}
