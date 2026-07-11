// synckit-agent (C#): generic host-side deploy agent. A consumer supplies a yaml pipeline + env vars;
// no consumer code needed. Ports the Go cmd/synckit-agent. Adds the portainer-update-stack step.
//
// Env:
//   DEPLOY_AGENT_SECRET  bearer secret for POST /deploy (required)
//   DEPLOY_AGENT_PORT    listen port (default 7777)
//   DEPLOY_AGENT_CONFIG  yaml path (default /etc/synckit/deploy-agent.yaml)
// Plus any env the steps read (PORTAINER_API_URL/KEY/STACK_ID/ENDPOINT_ID, notify webhook, etc).
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SyncKit.Agent;
using SyncKit.Bot;

var secret = Environment.GetEnvironmentVariable("DEPLOY_AGENT_SECRET");
if (string.IsNullOrEmpty(secret)) {
    Console.Error.WriteLine("synckit-agent: DEPLOY_AGENT_SECRET is required");
    return 1;
}
var port = Environment.GetEnvironmentVariable("DEPLOY_AGENT_PORT");
if (string.IsNullOrEmpty(port)) port = "7777";
var configPath = Environment.GetEnvironmentVariable("DEPLOY_AGENT_CONFIG");
if (string.IsNullOrEmpty(configPath)) configPath = "/etc/synckit/deploy-agent.yaml";

string yaml;
try { yaml = File.ReadAllText(configPath); } catch (Exception e) { Console.Error.WriteLine($"synckit-agent: read config: {e.Message}"); return 1; }

AgentConfig cfg;
try { cfg = AgentConfig.Parse(yaml); } catch (Exception e) { Console.Error.WriteLine($"synckit-agent: parse config: {e.Message}"); return 1; }
if (cfg.Steps.Count == 0) {
    Console.Error.WriteLine("synckit-agent: config has no steps");
    return 1;
}

var executor = new Executor { Repo = cfg.Repo, RepoUrl = cfg.RepoUrl, Steps = cfg.Steps };
var handler = new DeployHandler(executor.Run);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://*:{port}");
var app = builder.Build();

// POST /deploy: bearer-auth (constant-time), single-flight, returns the DeployResponse JSON.
app.MapPost("/deploy", (HttpRequest req) => {
    var token = (req.Headers.Authorization.ToString() ?? "").Replace("Bearer ", "");
    var ok = !string.IsNullOrEmpty(secret) && CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(secret));
    if (!ok) return Results.Text("unauthorized", "text/plain", null, StatusCodes.Status401Unauthorized);
    var (res, ran) = handler.TryRun();
    if (!ran) return Results.Text("deploy already in progress", "text/plain", null, StatusCodes.Status409Conflict);
    return Results.Json(res);
});

if (cfg.Watch is { } watch) {
    var resolveWebhookUrl = BuildWebhookResolver(watch);
    var watcher = new Watcher(cfg.Name, watch.Interval, resolveWebhookUrl, handler.TryRun);
    _ = watcher.RunAsync(app.Lifetime.ApplicationStopping);
    Console.WriteLine($"synckit-agent: watching every {watch.Interval}");
}

Console.WriteLine($"synckit-agent: {cfg.Name} listening on :{port} ({cfg.Steps.Count} steps)");
app.Run();
return 0;

// Resolves the webhook URL from ChannelHub's DB-stored per-thread webhook (created/rotated by
// SyncKit.Bot itself). Falls back to "" (silent) when guild/app/db connection aren't configured.
static Func<string> BuildWebhookResolver(WatchConfig watch) {
    var dbConn = Environment.GetEnvironmentVariable("IDENTITY_DB_CONNECTION");

    if (watch.NotifyChannelGuildId == "" || watch.NotifyChannelAppName == "" || string.IsNullOrEmpty(dbConn)) {
        Console.WriteLine("synckit-agent: watch notify disabled: guild/app/IDENTITY_DB_CONNECTION not configured");
        return () => "";
    }

    var store = new ChannelStateStore(NpgsqlDataSource.Create(dbConn));
    return () => {
        try {
            var webhook = store.GetAsync(watch.NotifyChannelGuildId, watch.NotifyChannelAppName,
                "thread:DeployNotifications:webhook", CancellationToken.None).GetAwaiter().GetResult();
            var thread = store.GetAsync(watch.NotifyChannelGuildId, watch.NotifyChannelAppName,
                "thread:DeployNotifications", CancellationToken.None).GetAwaiter().GetResult();
            if (webhook is null || thread is null) {
                Console.WriteLine("synckit-agent: resolve deploy webhook: no ChannelHub thread/webhook stored yet");
                return "";
            }
            return $"https://discord.com/api/webhooks/{webhook.DiscordId}/{webhook.WebhookToken}?thread_id={thread.DiscordId}";
        } catch (Exception e) {
            Console.Error.WriteLine($"synckit-agent: resolve deploy webhook: {e.Message}");
            return "";
        }
    };
}
