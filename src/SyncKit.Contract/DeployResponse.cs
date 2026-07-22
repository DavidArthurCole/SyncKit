using System.Text.Json.Serialization;

namespace SyncKit.Contract;

public sealed class DeployResponse {
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("alreadyUpToDate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AlreadyUpToDate { get; set; }

    [JsonPropertyName("tail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Tail { get; set; }

    [JsonPropertyName("fromHash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? FromHash { get; set; }

    [JsonPropertyName("toHash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ToHash { get; set; }

    [JsonPropertyName("fromUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? FromUrl { get; set; }

    [JsonPropertyName("toUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ToUrl { get; set; }
}
