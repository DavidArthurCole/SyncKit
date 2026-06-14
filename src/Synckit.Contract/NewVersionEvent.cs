using System.Text.Json.Serialization;

namespace Synckit.Contract;

// Mirrors the device-farm wire shape. The Go contract.NewVersionEvent has only the
// first five fields; EggIncognito's farm emits a SUPERSET. This C# port carries the
// full superset so EggLedger ignores extras and EggIncognito keeps receiving them.
// Extra fields are nullable / WhenWritingDefault so they vanish from output when unset.
public sealed class NewVersionEvent
{
    [JsonPropertyName("package")]
    public string Package { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("apkRef")]
    public string ApkRef { get; set; } = "";

    [JsonPropertyName("protoSha")]
    public string ProtoSha { get; set; } = "";

    [JsonPropertyName("detectedAt")]
    public string DetectedAt { get; set; } = "";

    [JsonPropertyName("appVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? AppVersion { get; set; }

    [JsonPropertyName("build")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Build { get; set; }

    [JsonPropertyName("clientVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ClientVersion { get; set; }

    [JsonPropertyName("platform")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Platform { get; set; }

    [JsonPropertyName("protoTextB64")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ProtoTextB64 { get; set; }
}
