using Discord;
using SyncKit.Bot;
using Xunit;

namespace SyncKit.Bot.Tests;

public class CommandRegistrationTests
{
    [Fact]
    public void BuiltinCommandNames_AreVerifyAndUpdateserver()
    {
        Assert.Equal(new[] { "verify", "updateserver" }, SyncKitBot.BuiltinCommandNames);
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
        var kept = SyncKitBot.FilterExtras(extras).Select(c => c.Name).ToArray();
        Assert.Equal(new[] { "mystats" }, kept);
    }

    [Theory]
    [InlineData(new[] { "a", "b" }, "c", true)]   // role absent -> needs it
    [InlineData(new[] { "a", "b" }, "b", false)]  // role present -> no-op
    public void NeedsRole_DetectsAbsence(string[] memberRoles, string roleId, bool expected)
    {
        Assert.Equal(expected, SyncKitBot.NeedsRole(memberRoles, roleId));
    }

    [Fact]
    public void GlobalCommands_Default_IsFalse()
    {
        var cfg = new BotConfig();
        Assert.False(cfg.GlobalCommands);
    }

    [Fact]
    public void FilterExtras_StillDropsBuiltins_WhenGlobalCommandsEnabled()
    {
        // GlobalCommands only changes the Discord API call shape (bulk-overwrite vs
        // per-command create), not the FilterExtras collision rule - verify the rule
        // is independent of the flag.
        var extras = new[] { MakeCmd("verify"), MakeCmd("mystats") };
        var kept = SyncKitBot.FilterExtras(extras).Select(c => c.Name).ToArray();
        Assert.Equal(new[] { "mystats" }, kept);
    }

    [Fact]
    public void FilterExtras_PreservesAutocompleteHandler()
    {
        var handler = (SocketAutocompleteContext _) => Task.CompletedTask;
        var cmd = new BotCommand(
            new SlashCommandBuilder().WithName("proto").WithDescription("d").Build(),
            "proto",
            _ => Task.CompletedTask,
            handler);

        var kept = SyncKitBot.FilterExtras(new[] { cmd }).Single();
        Assert.Same(handler, kept.AutocompleteHandler);
    }

    private static BotCommand MakeCmd(string name) =>
        new(new SlashCommandBuilder().WithName(name).WithDescription("d").Build(), name, _ => Task.CompletedTask);

    private static BotConfig BuildFixtureConfig(params BotCommand[] commands)
    {
        var builder = new SyncKitBotBuilder()
            .WithName("FixtureBot")
            .WithEnvFallback(_ => null);
        foreach (var c in commands) builder.WithCommand(c);
        return builder.BuildConfig();
    }

    [Fact]
    public void BuilderFixture_ProducesConfigWithSuppliedCommands()
    {
        var cfg = BuildFixtureConfig(MakeCmd("mystats"));
        Assert.Single(cfg.Extra);
        Assert.Equal("mystats", cfg.Extra[0].Name);
    }
}
