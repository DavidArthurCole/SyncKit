using SyncKit.Bot;
using SyncKit.Contract;
using Xunit;

namespace SyncKit.Bot.Tests;

public class EmbedsTests
{
    private static BotConfig Cfg() => new()
    {
        Name = "EggLedger",
        RepoUrl = "https://github.com/x/y",
        Build = new VerifyInfo { Sha256 = "deadbeef", Version = "v1.0.0", Date = "2026-06-14" },
    };

    [Fact]
    public void AlreadyUpToDate_BlurpleTitleAndColor()
    {
        var e = Embeds.AlreadyUpToDate(Cfg(), "abc1234");
        Assert.Equal("Already up to date.", e.Title);
        Assert.Equal(0x5865F2u, e.Color!.Value.RawValue);
        Assert.Contains(e.Fields, f => f.Name == "Current" && f.Value.ToString()!.Contains("abc1234"));
    }

    [Fact]
    public void Success_GreenFromTo()
    {
        var e = Embeds.Success(Cfg(), "aaa1111", "bbb2222");
        Assert.Equal("Updated", e.Title);
        Assert.Equal(0x57F287u, e.Color!.Value.RawValue);
        Assert.Contains(e.Fields, f => f.Name == "From" && f.Value.ToString()!.Contains("aaa1111"));
        Assert.Contains(e.Fields, f => f.Name == "To" && f.Value.ToString()!.Contains("bbb2222"));
    }

    [Fact]
    public void Failure_RedWithTail()
    {
        var e = Embeds.Failure("boom log");
        Assert.Equal("Update failed.", e.Title);
        Assert.Equal(0xED4245u, e.Color!.Value.RawValue);
        Assert.Contains("boom log", e.Description);
    }

    [Fact]
    public void Verify_BlurpleWithBuildFields()
    {
        var e = Embeds.Verify(Cfg());
        Assert.Equal("EggLedger Sync Server", e.Title);
        Assert.Equal(0x5865F2u, e.Color!.Value.RawValue);
        Assert.Contains(e.Fields, f => f.Name == "SHA256" && f.Value.ToString()!.Contains("deadbeef"));
        Assert.Contains(e.Fields, f => f.Name == "Version");
        Assert.Contains(e.Fields, f => f.Name == "Built" && f.Value.ToString() == "2026-06-14");
    }
}
