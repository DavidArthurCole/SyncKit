using System.Text.Json.Serialization;

namespace SyncKit.Contract;

// Host app builds one of these whenever its status changes (boot, post-deploy, timer) and
// hands it to SyncKitBot.UpdateDashboardAsync. Pure data; SyncKit.Bot owns rendering it.
public sealed class DashboardSnapshot
{
    [JsonPropertyName("appName")]
    public string AppName { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("buildHash")]
    public string BuildHash { get; set; } = "";

    [JsonPropertyName("deployStatus")]
    public string DeployStatus { get; set; } = "";

    [JsonPropertyName("uptimeSince")]
    public DateTimeOffset UptimeSince { get; set; }

    [JsonPropertyName("repoUrl")]
    public string RepoUrl { get; set; } = "";

    [JsonPropertyName("extraFields")]
    public IReadOnlyDictionary<string, string> ExtraFields { get; set; } = new Dictionary<string, string>();
}
