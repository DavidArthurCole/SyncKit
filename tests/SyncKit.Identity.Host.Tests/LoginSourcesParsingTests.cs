using System.Text.Json;
using Xunit;

namespace SyncKit.Identity.Host.Tests;

public class LoginSourcesParsingTests {
    private const string Authority = "https://auth.example.com";
    private const string StartUrl = "/login/start?mode=redirect&returnOrigin=https%3A%2F%2Fapp.example.com";

    private const string IdentificationStageJson = """
    {
      "component": "ak-stage-identification",
      "flow_designation": "authentication",
      "password_fields": false,
      "primary_action": "Log in",
      "show_source_labels": false,
      "user_fields": null,
      "sources": [
        {
          "name": "Discord",
          "icon_url": "/static/authentik/sources/discord.svg",
          "promoted": false,
          "challenge": {
            "component": "xak-flow-redirect",
            "to": "/source/oauth/login/discord/"
          }
        },
        {
          "name": "GitHub",
          "icon_url": "/static/authentik/sources/github.svg",
          "promoted": false,
          "challenge": {
            "component": "xak-flow-redirect",
            "to": "/source/oauth/login/github/"
          }
        },
        {
          "name": "Sign in with Apple",
          "icon_url": null,
          "promoted": false,
          "challenge": {
            "component": "ak-source-oauth-apple",
            "client_id": "com.example.app"
          }
        }
      ]
    }
    """;

    [Fact]
    public void ParseLoginSources_ExtractsRedirectSources_SkipsNonRedirectTypes() {
        using var doc = JsonDocument.Parse(IdentificationStageJson);

        var result = Program.ParseLoginSources(doc.RootElement, Authority, StartUrl);

        Assert.Equal(2, result.Count);
        Assert.Equal("Discord", result[0].Name);
        Assert.Equal("https://auth.example.com/static/authentik/sources/discord.svg", result[0].IconUrl);
        Assert.Equal(StartUrl, result[0].Url);
        Assert.Equal("GitHub", result[1].Name);
        Assert.Equal(StartUrl, result[1].Url);
        // "Sign in with Apple" (ak-source-oauth-apple, not xak-flow-redirect) is correctly skipped.
    }

    [Fact]
    public void ParseLoginSources_EmptySourcesArray_ReturnsEmptyList() {
        using var doc = JsonDocument.Parse("""{"component":"ak-stage-identification","password_fields":false,"sources":[]}""");

        var result = Program.ParseLoginSources(doc.RootElement, Authority, StartUrl);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseLoginSources_NullIconUrl_MapsToNull() {
        using var doc = JsonDocument.Parse("""
        {"component":"ak-stage-identification","password_fields":false,"sources":[
          {"name":"Google","icon_url":null,"promoted":false,"challenge":{"component":"xak-flow-redirect","to":"/source/oauth/login/google/"}}
        ]}
        """);

        var result = Program.ParseLoginSources(doc.RootElement, Authority, StartUrl);

        Assert.Single(result);
        Assert.Null(result[0].IconUrl);
    }

    [Fact]
    public void ParseLoginSources_NullSourcesValue_ReturnsEmptyList() {
        using var doc = JsonDocument.Parse("""{"component":"ak-stage-identification","password_fields":false,"sources":null}""");

        var result = Program.ParseLoginSources(doc.RootElement, Authority, StartUrl);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseLoginSources_NullChallengeValue_SkipsEntryWithoutThrowing() {
        using var doc = JsonDocument.Parse("""
        {"component":"ak-stage-identification","password_fields":false,"sources":[
          {"name":"Broken","icon_url":null,"promoted":false,"challenge":null},
          {"name":"Google","icon_url":null,"promoted":false,"challenge":{"component":"xak-flow-redirect","to":"/source/oauth/login/google/"}}
        ]}
        """);

        var result = Program.ParseLoginSources(doc.RootElement, Authority, StartUrl);

        Assert.Single(result);
        Assert.Equal("Google", result[0].Name);
    }
}
