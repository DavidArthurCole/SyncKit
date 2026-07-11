using Discord;
using SyncKit.Contract;

namespace SyncKit.Bot;

// Ports Go bot/embeds.go + VerifyResponse embed. Colors and titles frozen for visual parity.
// Renamed from Embeds so app code can call SyncKitBot.DefaultEmbeds.X inside a full-delegate
// override and layer changes on top instead of reimplementing from scratch.
public static class DefaultEmbeds {
    public static Embed AlreadyUpToDate(BotConfig cfg, string hash) =>
        new EmbedBuilder()
            .WithTitle("Already up to date.")
            .WithColor(new Color(0x5865F2))
            .AddField("Current", $"[`{hash}`]({cfg.CommitUrl(hash)})", inline: true)
            .Build();

    public static Embed Success(BotConfig cfg, string fromHash, string toHash) =>
        new EmbedBuilder()
            .WithTitle("Updated")
            .WithColor(new Color(0x57F287))
            .AddField("From", $"[`{fromHash}`]({cfg.CommitUrl(fromHash)})", inline: true)
            .AddField("To", $"[`{toHash}`]({cfg.CommitUrl(toHash)})", inline: true)
            .Build();

    public static Embed Failure(string tail) =>
        new EmbedBuilder()
            .WithTitle("Update failed.")
            .WithDescription($"```\n{tail}\n```")
            .WithColor(new Color(0xED4245))
            .Build();

    public static Embed Verify(BotConfig cfg) =>
        new EmbedBuilder()
            .WithTitle(cfg.Name + " Sync Server")
            .WithColor(new Color(0x5865F2))
            .AddField("SHA256", $"[{cfg.Build.Sha256}]({cfg.CommitUrl(cfg.Build.Version)})")
            .AddField("Version", $"[{cfg.Build.Version}]({cfg.CommitUrl(cfg.Build.Version)})", inline: true)
            .AddField("Built", string.IsNullOrEmpty(cfg.Build.Date) ? "unknown" : cfg.Build.Date, inline: true)
            .Build();

    public static Embed Dashboard(DashboardSnapshot snapshot) {
        var builder = new EmbedBuilder()
            .WithTitle(snapshot.AppName)
            .WithColor(new Color(0x5865F2))
            .AddField("Version", string.IsNullOrEmpty(snapshot.Version) ? "unknown" : snapshot.Version, inline: true)
            .AddField("Status", string.IsNullOrEmpty(snapshot.DeployStatus) ? "unknown" : snapshot.DeployStatus, inline: true)
            .AddField("Up since", $"<t:{snapshot.UptimeSince.ToUnixTimeSeconds()}:R>", inline: true);

        if (!string.IsNullOrEmpty(snapshot.BuildHash))
            builder.AddField("Build", $"`{snapshot.BuildHash}`", inline: true);
        if (!string.IsNullOrEmpty(snapshot.RepoUrl))
            builder.AddField("Repo", snapshot.RepoUrl, inline: true);
        foreach (var (key, value) in snapshot.ExtraFields)
            builder.AddField(key, value, inline: true);

        return builder.WithFooter("Updated").WithCurrentTimestamp().Build();
    }
}

// Tier-2 re-skin: overrides Color/Title on a SyncKit default embed and appends ExtraFields
// after the builtin ones. A full delegate (tier 3) always wins over this when both are set.
public sealed record EmbedOptions {
    public uint? Color { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<(string Name, string Value, bool Inline)> ExtraFields { get; init; } = Array.Empty<(string, string, bool)>();

    public Embed Apply(Embed defaultEmbed) {
        var builder = defaultEmbed.ToEmbedBuilder();
        if (Title is not null) builder.WithTitle(Title);
        if (Color is not null) builder.WithColor(new Discord.Color(Color.Value));
        foreach (var (name, value, inline) in ExtraFields)
            builder.AddField(name, value, inline);
        return builder.Build();
    }
}
