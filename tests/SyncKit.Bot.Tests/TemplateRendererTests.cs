using SyncKit.Bot;
using SyncKit.Contract;
using Xunit;

namespace SyncKit.Bot.Tests;

public class TemplateRendererTests {
    [Fact]
    public void Render_NullTemplate_ReturnsFallback() {
        var result = TemplateRenderer.Render(null, "fallback text", new DeployResponse(), "eggledger");
        Assert.Equal("fallback text", result);
    }

    [Fact]
    public void Render_EmptyTemplate_ReturnsFallback() {
        var result = TemplateRenderer.Render("", "fallback text", new DeployResponse(), "eggledger");
        Assert.Equal("fallback text", result);
    }

    [Fact]
    public void Render_ValidTemplate_SubstitutesFields() {
        var res = new DeployResponse { FromHash = "abc123", ToHash = "def456" };
        var result = TemplateRenderer.Render("From {{ from_hash }} to {{ to_hash }}", "fallback", res, "eggledger");
        Assert.Equal("From abc123 to def456", result);
    }

    [Fact]
    public void Render_AppNameVariable_Substitutes() {
        var result = TemplateRenderer.Render("Deployed {{ app_name }}", "fallback", new DeployResponse(), "eggledger");
        Assert.Equal("Deployed eggledger", result);
    }

    [Fact]
    public void Render_Conditional_BranchesOnOk() {
        const string template = "{{ if ok }}Success{{ else }}Failed{{ end }}";
        Assert.Equal("Success", TemplateRenderer.Render(template, "fallback", new DeployResponse { Ok = true }, "app"));
        Assert.Equal("Failed", TemplateRenderer.Render(template, "fallback", new DeployResponse { Ok = false }, "app"));
    }

    [Fact]
    public void Render_MissingField_RendersEmpty_DoesNotThrow() {
        var result = TemplateRenderer.Render("Tail: [{{ tail }}]", "fallback", new DeployResponse(), "app");
        Assert.Equal("Tail: []", result);
    }

    [Fact]
    public void Render_MalformedTemplate_ReturnsFallback() {
        var result = TemplateRenderer.Render("{{ if ok ", "fallback text", new DeployResponse(), "app");
        Assert.Equal("fallback text", result);
    }

    [Fact]
    public void Render_UnknownVariable_ReturnsFallback() {
        // Scriban renders unknown identifiers as empty by default rather than throwing;
        // this test locks in that a genuinely broken script (bad syntax) still falls back safely.
        var result = TemplateRenderer.Render("{{ this is not valid scriban !! }}", "fallback text", new DeployResponse(), "app");
        Assert.Equal("fallback text", result);
    }
}
