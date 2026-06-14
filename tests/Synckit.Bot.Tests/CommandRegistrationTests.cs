using Discord;
using Synckit.Bot;
using Xunit;

namespace Synckit.Bot.Tests;

public class CommandRegistrationTests
{
    [Fact]
    public void BuiltinCommandNames_AreVerifyAndUpdateserver()
    {
        Assert.Equal(new[] { "verify", "updateserver" }, SynckitBot.BuiltinCommandNames);
    }

    [Fact]
    public void FilterExtras_DropsBuiltinCollisions()
    {
        var extras = new[]
        {
            MakeCmd("verify"),     // collides, dropped
            MakeCmd("mystats"),    // kept
            MakeCmd("updateserver"), // collides, dropped
        };
        var kept = SynckitBot.FilterExtras(extras).Select(c => c.Name).ToArray();
        Assert.Equal(new[] { "mystats" }, kept);
    }

    [Theory]
    [InlineData(new[] { "a", "b" }, "c", true)]   // role absent -> needs it
    [InlineData(new[] { "a", "b" }, "b", false)]  // role present -> no-op
    public void NeedsRole_DetectsAbsence(string[] memberRoles, string roleId, bool expected)
    {
        Assert.Equal(expected, SynckitBot.NeedsRole(memberRoles, roleId));
    }

    private static BotCommand MakeCmd(string name) =>
        new(new SlashCommandBuilder().WithName(name).WithDescription("d").Build(), name, _ => Task.CompletedTask);
}
