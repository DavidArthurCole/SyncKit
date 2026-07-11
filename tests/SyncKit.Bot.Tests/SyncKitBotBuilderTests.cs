using Discord;
using SyncKit.Bot;
using SyncKit.Contract;
using Xunit;

namespace SyncKit.Bot.Tests;

public class SyncKitBotBuilderTests {
    [Fact]
    public void WithConfigFile_MissingFile_FallsBackToEnv_BuildsConfigWithEnvValues() {
        Environment.SetEnvironmentVariable("SYNCKIT_BOT_BUILDER_TEST_TOKEN", "env-token");
        try {
            var cfg = new SyncKitBotBuilder()
                .WithConfigFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".env"))
                .WithName("Test")
                .WithEnvFallback(key => key == "DISCORD_TOKEN"
                    ? Environment.GetEnvironmentVariable("SYNCKIT_BOT_BUILDER_TEST_TOKEN")
                    : null)
                .BuildConfig();

            Assert.Equal("env-token", cfg.Token);
            Assert.Equal("Test", cfg.Name);
        } finally { Environment.SetEnvironmentVariable("SYNCKIT_BOT_BUILDER_TEST_TOKEN", null); }
    }

    [Fact]
    public void WithCommand_AddsToExtra() {
        var cmd = new BotCommand(
            new SlashCommandBuilder().WithName("mystats").WithDescription("d").Build(),
            "mystats",
            _ => Task.CompletedTask);

        var cfg = new SyncKitBotBuilder()
            .WithName("Test")
            .WithEnvFallback(_ => null)
            .WithCommand(cmd)
            .BuildConfig();

        Assert.Single(cfg.Extra);
        Assert.Equal("mystats", cfg.Extra[0].Name);
    }

    [Fact]
    public void WithGlobalCommands_SetsFlag() {
        var cfg = new SyncKitBotBuilder()
            .WithName("Test")
            .WithEnvFallback(_ => null)
            .WithGlobalCommands(true)
            .BuildConfig();

        Assert.True(cfg.GlobalCommands);
    }

    [Fact]
    public void WithBuild_SetsVerifyInfo() {
        var build = new VerifyInfo { Sha256 = "abc", Version = "v1", Date = "2026-01-01" };

        var cfg = new SyncKitBotBuilder()
            .WithName("Test")
            .WithEnvFallback(_ => null)
            .WithBuild(build)
            .BuildConfig();

        Assert.Equal("abc", cfg.Build.Sha256);
    }

    [Fact]
    public void VerifyEmbed_DelegateWinsOverOptions_WhenBothSet() {
        var builder = new SyncKitBotBuilder()
            .WithName("Test")
            .WithEnvFallback(_ => null)
            .WithVerifyEmbed(new EmbedOptions { Title = "From Options" })
            .WithVerifyEmbedBuilder(cfg => new EmbedBuilder().WithTitle("From Delegate").Build());

        var resolved = builder.ResolveVerifyEmbed(builder.BuildConfig());
        Assert.Equal("From Delegate", resolved.Title);
    }

    [Fact]
    public void VerifyEmbed_OptionsOnly_AppliesOverDefault() {
        var builder = new SyncKitBotBuilder()
            .WithName("Test")
            .WithEnvFallback(_ => null)
            .WithVerifyEmbed(new EmbedOptions { Title = "From Options" });

        var resolved = builder.ResolveVerifyEmbed(builder.BuildConfig());
        Assert.Equal("From Options", resolved.Title);
    }

    [Fact]
    public void VerifyEmbed_NoOverride_UsesDefault() {
        var builder = new SyncKitBotBuilder()
            .WithName("Test")
            .WithEnvFallback(_ => null);

        var cfg = builder.BuildConfig();
        var resolved = builder.ResolveVerifyEmbed(cfg);
        Assert.Equal(DefaultEmbeds.Verify(cfg).Title, resolved.Title);
    }
}
