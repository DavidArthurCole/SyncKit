using System.Text.Json.Serialization;

namespace SyncKit.Contract;

public sealed class VerifyInfo {
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";
}
