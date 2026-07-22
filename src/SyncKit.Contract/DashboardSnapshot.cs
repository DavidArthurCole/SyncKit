using System.Text.Json.Serialization;

namespace SyncKit.Contract;

public sealed class DashboardSnapshot {
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
