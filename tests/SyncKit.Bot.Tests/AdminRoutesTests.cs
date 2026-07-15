using SyncKit.Bot;
using Xunit;

namespace SyncKit.Bot.Tests;

public class AdminRoutesTests {
    [Fact]
    public void AdminConfigRequest_HoldsAllFields() {
        var req = new AdminConfigRequest(
            DashboardChannelId: "111",
            GithubFeedThreadId: "222",
            DeployNotificationsThreadId: "333",
            SuccessEmbedJson: "{\"title\":\"ok\"}",
            FailureEmbedJson: "{\"title\":\"fail\"}",
            UptodateEmbedJson: "{\"title\":\"utd\"}");

        Assert.Equal("111", req.DashboardChannelId);
        Assert.Equal("222", req.GithubFeedThreadId);
        Assert.Equal("333", req.DeployNotificationsThreadId);
    }

    [Fact]
    public void AdminConfigResponse_FromChannelConfig_MapsPersistedFieldsAndDefaults() {
        var cc = new ChannelConfig("g1", "app1", "111", "222", "333", "sj", "fj", "uj");
        var resp = AdminConfigResponse.From(cc, "111", "222", "333", "http://hook");

        Assert.Equal("111", resp.DashboardChannelId);
        Assert.Equal("222", resp.GithubFeedThreadId);
        Assert.Equal("333", resp.DeployNotificationsThreadId);
        Assert.Equal("sj", resp.SuccessEmbedJson);
        Assert.Equal("fj", resp.FailureEmbedJson);
        Assert.Equal("uj", resp.UptodateEmbedJson);
        Assert.Equal("http://hook", resp.GithubWebhookUrl);
        Assert.NotNull(resp.DefaultSuccess);
        Assert.NotEmpty(resp.Variables);
    }

    [Fact]
    public void AdminConfigResponse_FromNullChannelConfig_EmbedJsonNullDefaultsPresent() {
        var resp = AdminConfigResponse.From(null, null, null, null, null);

        Assert.Null(resp.DashboardChannelId);
        Assert.Null(resp.SuccessEmbedJson);
        Assert.NotNull(resp.DefaultFailure);
        Assert.NotEmpty(resp.Variables);
    }
}
