using System.Text.Json;
using SyncKit.Bot;
using SyncKit.Contract;
using Xunit;

namespace SyncKit.Bot.Tests;

public class MessageRendererTests {
    private static Dictionary<string, object?> Vars() =>
        new() { ["app_name"] = "EggLedger", ["to_hash"] = "abc1234" };

    [Fact]
    public void EmbedKind_RendersEmbed_NoComponents() {
        var spec = MessageSpec.FromEmbed(DeployEmbedDefaults.Success);
        var rendered = MessageRenderer.Render(spec, Vars());

        Assert.False(rendered.IsComponentsV2);
        Assert.NotNull(rendered.Embed);
        Assert.Null(rendered.Components);
    }

    [Fact]
    public void ComponentsKind_RendersComponents_NoEmbed() {
        var spec = new MessageSpec(
            MessageKind.Components, null,
            new ComponentSpec(0x57F287, new List<ComponentBlock> {
                new("text", "Deployed {{ to_hash }}", null, true),
                new("separator", null, null, true),
            }),
            null);

        var rendered = MessageRenderer.Render(spec, Vars());

        Assert.True(rendered.IsComponentsV2);
        Assert.NotNull(rendered.Components);
        Assert.Null(rendered.Embed);
    }

    [Fact]
    public void Mentions_MapToAllowedMentions() {
        var spec = new MessageSpec(
            MessageKind.Components,
            null,
            new ComponentSpec(null, new List<ComponentBlock> { new("text", "{{ pings }}", null, true) }),
            new MentionSpec(new[] { "123" }, new[] { "456" }, true));

        var rendered = MessageRenderer.Render(spec, Vars());

        Assert.Contains(123ul, rendered.AllowedMentions.UserIds!);
        Assert.Contains(456ul, rendered.AllowedMentions.RoleIds!);
        Assert.True(rendered.AllowedMentions.AllowedTypes!.Value.HasFlag(Discord.AllowedMentionTypes.Everyone));
    }

    [Fact]
    public void PingsToken_ExpandsMentionStrings() {
        var spec = new MessageSpec(
            MessageKind.Embed,
            new EmbedSpec(null, null, null, null, "{{ pings }}", null, null,
                new List<EmbedFieldSpec>(), null, null, null, null, false),
            null,
            new MentionSpec(new[] { "7" }, new[] { "9" }, false));

        var rendered = MessageRenderer.Render(spec, Vars());

        Assert.Equal("<@&9> <@7>", rendered.Embed!.Title);
    }

    [Fact]
    public void FromEmbed_JsonRoundTrips() {
        var spec = MessageSpec.FromEmbed(DeployEmbedDefaults.Success);
        var json = JsonSerializer.Serialize(spec);
        var back = JsonSerializer.Deserialize<MessageSpec>(json);

        Assert.NotNull(back);
        Assert.Equal(MessageKind.Embed, back!.Kind);
        Assert.NotNull(back.Embed);
        Assert.Contains("\"kind\":\"embed\"", json);
    }

    [Fact]
    public void PartialJson_NullInnerLists_DoesNotThrow() {
        var spec = JsonSerializer.Deserialize<MessageSpec>(
            "{\"kind\":\"components\",\"components\":{},\"mentions\":{\"everyone\":true}}")!;

        var rendered = MessageRenderer.Render(spec, Vars());

        Assert.True(rendered.IsComponentsV2);
        Assert.NotNull(rendered.Components);
        Assert.True(rendered.AllowedMentions.AllowedTypes!.Value.HasFlag(Discord.AllowedMentionTypes.Everyone));
    }

    [Fact]
    public void Resolve_PrefersMessage_ThenEmbed_ThenDefault() {
        var msg = "{\"kind\":\"components\",\"components\":{\"blocks\":[]}}";
        var emb = "{\"title\":\"legacy\"}";

        Assert.Equal(MessageKind.Components, MessageSpecs.Resolve(msg, emb, DeployEmbedDefaults.Success).Kind);
        Assert.Equal("legacy", MessageSpecs.Resolve(null, emb, DeployEmbedDefaults.Success).Embed!.Title);
        Assert.Equal(MessageKind.Embed, MessageSpecs.Resolve(null, null, DeployEmbedDefaults.Success).Kind);
    }
}
