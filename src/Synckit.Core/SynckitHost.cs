using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Synckit.Bot;
using Synckit.Db;

namespace Synckit.Core;

// Ports Go synckit.Run: optional DB init+migrate, start bot (best-effort), wire the
// new-version route, listen on LISTEN_ADDR (default :8080), block until shutdown.
public static class SynckitHost
{
    public static async Task RunAsync(AppProfile profile, Action<WebApplication>? configureRoutes = null)
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        if (profile.Db is not null)
        {
            var conn = await Database.InitAsync(profile.Db.ConnStr);
            if (!string.IsNullOrEmpty(profile.Db.MigrationsDir))
                await Migrator.MigrateAsync(conn, profile.Db.MigrationsDir);
            app.Lifetime.ApplicationStopping.Register(() => conn.Dispose());
        }

        SynckitBot? bot = null;
        try
        {
            bot = await SynckitBot.StartAsync(MapBotConfig(profile));
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "synckit: bot start failed, continuing");
        }
        if (bot is not null)
            app.Lifetime.ApplicationStopping.Register(() => bot.DisposeAsync().AsTask().GetAwaiter().GetResult());

        if (profile.Events.NewVersion is not null)
            app.MapPost("/events/new-version",
                NewVersionHandler.Build(profile.Events.EventSecret, profile.Events.NewVersion));

        configureRoutes?.Invoke(app);

        var addr = Environment.GetEnvironmentVariable("LISTEN_ADDR");
        if (string.IsNullOrEmpty(addr)) addr = ":8080";
        var urls = addr.StartsWith(':') ? $"http://0.0.0.0{addr}" : $"http://{addr}";

        app.Logger.LogInformation("synckit: {Name} listening on {Addr}", profile.Name, addr);
        await app.RunAsync(urls);
    }

    private static BotConfig MapBotConfig(AppProfile p) => new()
    {
        Name = p.Name,
        Token = p.Discord.Token,
        AppId = p.Discord.AppId,
        GuildId = p.Discord.GuildId,
        RepoUrl = p.RepoUrl,
        Build = p.Build,
        DeployAgentUrl = p.DeployAgent.Url,
        DeployAgentSecret = p.DeployAgent.Secret,
        SharedRoleId = p.Discord.SharedRoleId,
        Extra = p.Commands,
    };
}
