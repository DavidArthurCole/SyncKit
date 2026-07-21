using SyncKit.Bot;
using SyncKit.Contract;
using Xunit;

namespace SyncKit.Bot.Tests;

public class DashboardSignatureTests {
    private static DashboardSnapshot Sample() => new() {
        AppName = "EggLedger",
        Version = "0.7.0-preview.1",
        BuildHash = "abc1234",
        DeployStatus = "running",
        UptimeSince = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
        RepoUrl = "https://github.com/x/y",
        ExtraFields = new Dictionary<string, string> { ["queue"] = "3", ["captures"] = "7" },
    };

    [Fact]
    public void SameSnapshot_SameSignature() {
        Assert.Equal(DashboardSignature.Of(Sample()), DashboardSignature.Of(Sample()));
    }

    [Fact]
    public void ChangedField_ChangesSignature() {
        var a = Sample();
        var b = Sample();
        b.Version = "0.7.0-preview.2";
        Assert.NotEqual(DashboardSignature.Of(a), DashboardSignature.Of(b));
    }

    [Fact]
    public void ChangedExtraFieldValue_ChangesSignature() {
        var a = Sample();
        var b = Sample();
        b.ExtraFields = new Dictionary<string, string> { ["queue"] = "4", ["captures"] = "7" };
        Assert.NotEqual(DashboardSignature.Of(a), DashboardSignature.Of(b));
    }

    [Fact]
    public void ExtraFieldOrder_DoesNotChangeSignature() {
        var a = Sample();
        a.ExtraFields = new Dictionary<string, string> { ["queue"] = "3", ["captures"] = "7" };
        var b = Sample();
        b.ExtraFields = new Dictionary<string, string> { ["captures"] = "7", ["queue"] = "3" };
        Assert.Equal(DashboardSignature.Of(a), DashboardSignature.Of(b));
    }

    [Fact]
    public void AdjacentFieldBoundaries_AreUnambiguous() {
        var a = Sample();
        a.AppName = "ab";
        a.Version = "c";
        var b = Sample();
        b.AppName = "a";
        b.Version = "bc";
        Assert.NotEqual(DashboardSignature.Of(a), DashboardSignature.Of(b));
    }
}
