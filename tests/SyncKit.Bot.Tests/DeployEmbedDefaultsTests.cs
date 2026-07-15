using SyncKit.Bot;
using SyncKit.Contract;
using Xunit;

namespace SyncKit.Bot.Tests;

public class DeployEmbedDefaultsTests {
    [Fact]
    public void Success_RendersUpdatedWithFromToChips() {
        var res = new DeployResponse { FromHash = "abc", ToHash = "def", FromUrl = "u1", ToUrl = "u2" };
        var embed = EmbedRenderer.Render(DeployEmbedDefaults.Success, res, "app");

        Assert.Equal("Updated", embed.Title);
        Assert.Equal(0x57F287u, embed.Color!.Value.RawValue);
        Assert.Equal("From", embed.Fields[0].Name);
        Assert.Contains("abc", embed.Fields[0].Value);
        Assert.Contains("def", embed.Fields[1].Value);
    }

    [Fact]
    public void Failure_RendersUpdateFailedWithTail() {
        var embed = EmbedRenderer.Render(DeployEmbedDefaults.Failure, new DeployResponse { Tail = "boom" }, "app");

        Assert.Equal("Update failed.", embed.Title);
        Assert.Equal(0xED4245u, embed.Color!.Value.RawValue);
        Assert.Contains("boom", embed.Description);
    }

    [Fact]
    public void AlreadyUpToDate_RendersCurrentField() {
        var res = new DeployResponse { ToHash = "def", ToUrl = "u2" };
        var embed = EmbedRenderer.Render(DeployEmbedDefaults.AlreadyUpToDate, res, "app");

        Assert.Equal("Already up to date.", embed.Title);
        Assert.Equal(0x5865F2u, embed.Color!.Value.RawValue);
        Assert.Equal("Current", embed.Fields[0].Name);
        Assert.Contains("def", embed.Fields[0].Value);
    }

    [Fact]
    public void Variables_ListsExpectedNames() {
        var names = new HashSet<string>();
        foreach (var (name, _) in DeployEmbedDefaults.Variables) names.Add(name);

        Assert.Contains("ok", names);
        Assert.Contains("tail", names);
        Assert.Contains("from_hash", names);
        Assert.Contains("to_hash", names);
        Assert.Contains("app_name", names);
    }
}
