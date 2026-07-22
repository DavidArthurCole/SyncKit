using System.Text.Json;
using SyncKit.Contract;
using Xunit;

namespace SyncKit.Contract.Tests;

public class WireFormatTests {
    [Fact]
    public void DeployResponse_MatchesGoWire() {
        var dr = new DeployResponse { Ok = true, AlreadyUpToDate = true, FromHash = "abc1234", ToHash = "abc1234" };
        var json = JsonSerializer.Serialize(dr);
        Assert.Equal(
            "{\"ok\":true,\"alreadyUpToDate\":true,\"fromHash\":\"abc1234\",\"toHash\":\"abc1234\"}",
            json);
    }

    [Fact]
    public void DeployResponse_Empty_EmitsOnlyOk() {
        var json = JsonSerializer.Serialize(new DeployResponse { Ok = false });
        Assert.Equal("{\"ok\":false}", json);
    }

    [Fact]
    public void NewVersionEvent_MatchesGoWire() {
        var e = new NewVersionEvent {
            Package = "com.auxbrain.egginc",
            Version = "1.34",
            ApkRef = "/x/base.apk",
            ProtoSha = "deadbeef",
            DetectedAt = "2026-06-10T00:00:00Z"
        };
        var json = JsonSerializer.Serialize(e);
        Assert.Equal(
            "{\"package\":\"com.auxbrain.egginc\",\"version\":\"1.34\",\"apkRef\":\"/x/base.apk\",\"protoSha\":\"deadbeef\",\"detectedAt\":\"2026-06-10T00:00:00Z\"}",
            json);
    }

    [Fact]
    public void VerifyInfo_AllFieldsAlwaysPresent() {
        var json = JsonSerializer.Serialize(new VerifyInfo { Name = "EggLedger", Sha256 = "ab", Version = "v1", Date = "d" });
        Assert.Equal("{\"name\":\"EggLedger\",\"sha256\":\"ab\",\"version\":\"v1\",\"date\":\"d\"}", json);
    }
}
