using SyncKit.Bot;
using Xunit;

namespace SyncKit.Bot.Tests;

public class DeployAgentClientTests
{
    [Fact]
    public void Parse_ReadsFrozenWire()
    {
        var r = DeployAgentClient.Parse(
            "{\"ok\":true,\"alreadyUpToDate\":true,\"fromHash\":\"abc1234\",\"toHash\":\"abc1234\"}");
        Assert.True(r.Ok);
        Assert.True(r.AlreadyUpToDate);
        Assert.Equal("abc1234", r.FromHash);
        Assert.Equal("abc1234", r.ToHash);
    }

    [Fact]
    public void Parse_BadJson_ReturnsTail()
    {
        var r = DeployAgentClient.Parse("not json");
        Assert.False(r.Ok);
        Assert.Equal("could not decode deploy agent response", r.Tail);
    }
}
