using System.Text;
using System.Text.Json;
using SyncKit.Contract;

namespace SyncKit.Agent;

// Polls the deploy pipeline on an interval and posts Discord webhook embeds on change.
// A failure posts red once per distinct tail, reset on any non-failure, so a broken pipeline doesn't spam every tick.
// resolveWebhookUrl is called fresh each notifying tick (not cached) so it can pick up a rotated ChannelHub webhook without restarting the agent.
public sealed class Watcher(string name, TimeSpan interval, Func<string> resolveWebhookUrl, Func<(DeployResponse, bool)> tryRun)
{
    private string _lastFail = "";

    public Watcher(string name, TimeSpan interval, string webhookUrl, Func<(DeployResponse, bool)> tryRun)
        : this(name, interval, () => webhookUrl, tryRun)
    {
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                Tick();
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    internal void Tick()
    {
        var (res, ran) = tryRun();
        if (!ran) return; // a manual deploy holds the lock; skip this tick
        var payload = Decide(res);
        if (payload is null) return;
        var webhookUrl = resolveWebhookUrl();
        if (string.IsNullOrEmpty(webhookUrl)) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            http.PostAsync(webhookUrl, content).GetAwaiter().GetResult();
        }
        catch (Exception e) { Console.Error.WriteLine($"watcher: notify: {e.Message}"); }
    }

    // Maps a result to a webhook payload (null = silent) and updates dedupe state.
    internal string? Decide(DeployResponse res)
    {
        if (res.Ok && res.AlreadyUpToDate) { _lastFail = ""; return null; }
        if (res.Ok) { _lastFail = ""; return Payload(SuccessEmbed(res)); }
        // Transient infra/network failures (Docker daemon down, GHCR/registry timeouts) are not deploy
        // failures the user can act on. Stay silent and leave _lastFail untouched so a genuine build
        // failure once infra recovers still posts.
        if (IsTransient(res.Tail)) return null;
        if (res.Tail == _lastFail) return null;
        _lastFail = res.Tail ?? "";
        return Payload(FailureEmbed(res));
    }

    // Substrings that mark an infra/network hiccup rather than an actionable deploy failure. Matched
    // anywhere in the tail because docker/compose/pull errors nest the cause inside wrapper text.
    private static readonly string[] TransientMarkers =
    {
        "Cannot connect to the Docker daemon", // host stopped Docker
        "context deadline exceeded",           // GHCR/registry pull timed out (the spam we saw)
        "Client.Timeout exceeded",             // Go http client timeout wrapper
        "TLS handshake timeout",
        "connection refused",
        "no such host",                        // DNS hiccup resolving the registry
        "i/o timeout",
        "temporary failure in name resolution",
    };

    // True when the tail looks like a transient infra/network error (registry unreachable, daemon down).
    internal static bool IsTransient(string? tail) =>
        tail is not null && TransientMarkers.Any(m => tail.Contains(m, StringComparison.OrdinalIgnoreCase));

    private string Payload(object embed)
    {
        var body = new Dictionary<string, object> { ["embeds"] = new[] { embed } };
        if (name != "") body["username"] = name;
        return JsonSerializer.Serialize(body);
    }

    private static object SuccessEmbed(DeployResponse res) => new
    {
        title = "Auto-deployed",
        color = 0x57F287,
        fields = new[]
        {
            new { name = "From", value = Chip(res.FromHash, res.FromUrl), inline = true },
            new { name = "To", value = Chip(res.ToHash, res.ToUrl), inline = true },
        },
    };

    private static object FailureEmbed(DeployResponse res) => new
    {
        title = "Auto-deploy failed.",
        description = $"```\n{(string.IsNullOrEmpty(res.Tail) ? "(no output)" : res.Tail)}\n```",
        color = 0xED4245,
    };

    private static string Chip(string? hash, string? url)
    {
        var h = string.IsNullOrEmpty(hash) ? "unknown" : hash;
        return string.IsNullOrEmpty(url) ? $"`{h}`" : $"[`{h}`]({url})";
    }
}
