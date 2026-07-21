using SyncKit.Bot;
using SyncKit.Contract;
using Xunit;

namespace SyncKit.Bot.Tests;

public class TemplateVarsTests {
    [Fact]
    public void DeployVars_ExposesDeployTokens() {
        var res = new DeployResponse { Ok = true, FromHash = "aaa", ToHash = "bbb" };
        var vars = DeployVars.Build(res, "EggLedger");
        Assert.Equal(true, vars["ok"]);
        Assert.Equal("aaa", vars["from_hash"]);
        Assert.Equal("bbb", vars["to_hash"]);
        Assert.Equal("EggLedger", vars["app_name"]);
    }

    [Fact]
    public void DashboardVars_ExposesDashboardTokensAndExtras() {
        var snapshot = new DashboardSnapshot {
            AppName = "EGI",
            Version = "2.0.0",
            BuildHash = "deadbeef",
            DeployStatus = "online",
            UptimeSince = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
            RepoUrl = "https://github.com/x/y",
            ExtraFields = new Dictionary<string, string> { ["Mode"] = "Hosted" },
        };

        var rendered = TemplateRenderer.RenderOrEmpty(
            "{{ app_name }}|{{ version }}|{{ build_hash }}|{{ deploy_status }}|{{ up_since_unix }}|{{ repo_url }}|{{ extra.Mode }}",
            DashboardVars.Build(snapshot));

        Assert.Equal("EGI|2.0.0|deadbeef|online|1700000000|https://github.com/x/y|Hosted", rendered);
    }

    [Fact]
    public void TemplateRenderer_UnknownToken_RendersEmpty() {
        var rendered = TemplateRenderer.RenderOrEmpty("[{{ missing }}]", DashboardVars.Build(new DashboardSnapshot()));
        Assert.Equal("[]", rendered);
    }

    [Fact]
    public void TemplateRenderer_BrokenTemplate_FallsBack() {
        var vars = DeployVars.Build(new DeployResponse(), "x");
        Assert.Equal("fallback", TemplateRenderer.Render("{{ if }}", "fallback", vars));
        Assert.Equal("", TemplateRenderer.RenderOrEmpty("{{ if }}", vars));
    }
}
