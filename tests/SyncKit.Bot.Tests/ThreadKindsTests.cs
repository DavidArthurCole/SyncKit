using SyncKit.Bot;
using Xunit;

namespace SyncKit.Bot.Tests;

public class ThreadKindsTests {
    [Fact]
    public void ParseCsv_EmptyOrNull_ReturnsEmpty() {
        Assert.Empty(ThreadKinds.ParseCsv(null));
        Assert.Empty(ThreadKinds.ParseCsv(""));
        Assert.Empty(ThreadKinds.ParseCsv("   "));
    }

    [Fact]
    public void ParseCsv_ValidNames_ParsesInOrder() {
        var result = ThreadKinds.ParseCsv("GithubFeed,DeployNotifications");
        Assert.Equal(new[] { ThreadKind.GithubFeed, ThreadKind.DeployNotifications }, result);
    }

    [Fact]
    public void ParseCsv_CaseInsensitiveAndWhitespaceTolerant() {
        var result = ThreadKinds.ParseCsv(" githubfeed , DEPLOYNOTIFICATIONS ");
        Assert.Equal(new[] { ThreadKind.GithubFeed, ThreadKind.DeployNotifications }, result);
    }

    [Fact]
    public void ParseCsv_UnknownNames_Skipped() {
        var result = ThreadKinds.ParseCsv("GithubFeed,BogusKind,DeployNotifications");
        Assert.Equal(new[] { ThreadKind.GithubFeed, ThreadKind.DeployNotifications }, result);
    }

    [Fact]
    public void ParseCsv_Duplicates_Deduplicated() {
        var result = ThreadKinds.ParseCsv("GithubFeed,GithubFeed");
        Assert.Equal(new[] { ThreadKind.GithubFeed }, result);
    }

    [Fact]
    public void ToName_MatchesEnumName() {
        Assert.Equal("GithubFeed", ThreadKinds.ToName(ThreadKind.GithubFeed));
    }
}
