using SyncKit.Bot;
using Xunit;

namespace SyncKit.Bot.Tests;

public class ThreadKindsTests {
    [Fact]
    public void ToName_MatchesEnumName() {
        Assert.Equal("GithubFeed", ThreadKinds.ToName(ThreadKind.GithubFeed));
        Assert.Equal("DeployNotifications", ThreadKinds.ToName(ThreadKind.DeployNotifications));
    }
}
