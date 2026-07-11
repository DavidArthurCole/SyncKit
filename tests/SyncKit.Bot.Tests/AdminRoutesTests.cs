using SyncKit.Bot;
using Xunit;

namespace SyncKit.Bot.Tests;

public class AdminRoutesTests {
    [Fact]
    public void AdminConfigRequest_RoundTripsThroughChannelConfig() {
        var req = new AdminConfigRequest(
            DashboardChannelId: "111",
            EnabledThreads: "DeployNotifications",
            SuccessTemplate: "{{ to_hash }}",
            FailureTemplate: "{{ tail }}",
            AlreadyUpToDateTemplate: "up to date");

        Assert.Equal("111", req.DashboardChannelId);
        Assert.Equal("DeployNotifications", req.EnabledThreads);
    }

    [Fact]
    public void AdminConfigResponse_FromChannelConfig_MapsAllFields() {
        var cc = new ChannelConfig("g1", "app1", "111", "DeployNotifications", "s", "f", "u");
        var resp = AdminConfigResponse.From(cc);

        Assert.Equal("111", resp.DashboardChannelId);
        Assert.Equal("DeployNotifications", resp.EnabledThreads);
        Assert.Equal("s", resp.SuccessTemplate);
        Assert.Equal("f", resp.FailureTemplate);
        Assert.Equal("u", resp.AlreadyUpToDateTemplate);
    }

    [Fact]
    public void AdminConfigResponse_FromNullChannelConfig_AllFieldsNull() {
        var resp = AdminConfigResponse.From(null);

        Assert.Null(resp.DashboardChannelId);
        Assert.Null(resp.EnabledThreads);
        Assert.Null(resp.SuccessTemplate);
    }
}
