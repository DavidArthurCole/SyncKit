using System.Text;
using System.Text.Json;
using SyncKit.Contract;

namespace SyncKit.Agent;

// Polls the deploy pipeline on an interval and posts Discord webhook embeds on change. Up-to-date is
// silent; a deploy posts green; a failure posts red once per distinct tail (reset on any non-failure)
// so a broken pipeline does not spam every tick. Mirrors the Go Watcher.
public sealed class Watcher(string name, TimeSpan interval, string webhookUrl, Func<(DeployResponse, bool)> tryRun)
{
    private string _lastFail = "";

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
        if (webhookUrl == "") return;
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
        // Docker daemon down (host stopped Docker) is infra-not-running, not a deploy failure. Stay silent
        // and leave _lastFail untouched so a genuine failure once Docker returns still posts.
        if (IsDockerDown(res.Tail)) return null;
        if (res.Tail == _lastFail) return null;
        _lastFail = res.Tail ?? "";
        return Payload(FailureEmbed(res));
    }

    // Matches the daemon-unreachable message from any docker/compose CLI call in the tail.
    internal static bool IsDockerDown(string? tail) =>
        tail is not null && tail.Contains("Cannot connect to the Docker daemon", StringComparison.Ordinal);

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
