using Discord;
using SyncKit.Bot;
using Xunit;

namespace SyncKit.Bot.Tests;

public class BotConfigTests {
    [Fact]
    public void Defaults_GlobalCommandsAndGuildMirrorFalse_SupporterRoleEmpty() {
        var cfg = new BotConfig();

        Assert.False(cfg.GlobalCommands);
        Assert.False(cfg.GuildCommandMirror);
        Assert.Equal("", cfg.SupporterRoleId);
    }

    [Fact]
    public void BotCommand_AutocompleteHandler_DefaultsToNull() {
        var cmd = new BotCommand(
            new SlashCommandBuilder().WithName("x").WithDescription("d").Build(),
            "x",
            _ => Task.CompletedTask);

        Assert.Null(cmd.AutocompleteHandler);
    }
}
