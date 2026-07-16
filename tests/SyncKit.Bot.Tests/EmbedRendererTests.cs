using System;
using System.Collections.Generic;
using SyncKit.Bot;
using SyncKit.Contract;
using Xunit;

namespace SyncKit.Bot.Tests;

public class EmbedRendererTests {
    private static EmbedSpec Spec(
        string? title = null, string? description = null,
        IReadOnlyList<EmbedFieldSpec>? fields = null, uint? color = null, bool timestamp = false,
        DateTimeOffset? timestampFixed = null) =>
        new(color, null, null, null, title, null, description,
            fields ?? new List<EmbedFieldSpec>(), null, null, null, null, timestamp, timestampFixed);

    [Fact]
    public void Render_SubstitutesVariablesInFields() {
        var res = new DeployResponse { FromHash = "abc", ToHash = "def" };
        var spec = Spec(fields: new List<EmbedFieldSpec> {
            new("From", "{{ from_hash }}", true),
            new("To", "{{ to_hash }}", true),
        });

        var embed = EmbedRenderer.Render(spec, res, "app");

        Assert.Equal("abc", embed.Fields[0].Value);
        Assert.Equal("def", embed.Fields[1].Value);
    }

    [Fact]
    public void Render_EmptyRenderedField_Skipped() {
        var spec = Spec(fields: new List<EmbedFieldSpec> {
            new("Present", "value", false),
            new("Blank", "{{ tail }}", false),
        });

        var embed = EmbedRenderer.Render(spec, new DeployResponse(), "app");

        Assert.Single(embed.Fields);
        Assert.Equal("Present", embed.Fields[0].Name);
    }

    [Fact]
    public void Render_BrokenScriban_FallsBackPerFieldToEmpty_Skipped() {
        var spec = Spec(description: "{{ if ok ");

        var embed = EmbedRenderer.Render(spec, new DeployResponse(), "app");

        Assert.True(string.IsNullOrEmpty(embed.Description));
    }

    [Fact]
    public void Render_MoreThan25Fields_TruncatesTo25() {
        var many = new List<EmbedFieldSpec>();
        for (var i = 0; i < 40; i++) many.Add(new EmbedFieldSpec($"n{i}", "v", false));

        var embed = EmbedRenderer.Render(Spec(fields: many), new DeployResponse(), "app");

        Assert.Equal(25, embed.Fields.Length);
    }

    [Fact]
    public void Render_SetsColorAndTitle() {
        var embed = EmbedRenderer.Render(Spec(title: "Hello", color: 0x57F287), new DeployResponse(), "app");

        Assert.Equal("Hello", embed.Title);
        Assert.Equal(0x57F287u, embed.Color!.Value.RawValue);
    }

    [Fact]
    public void Render_TimestampOff_NoTimestamp() {
        var embed = EmbedRenderer.Render(Spec(timestamp: false), new DeployResponse(), "app");
        Assert.Null(embed.Timestamp);
    }

    [Fact]
    public void Render_TimestampOn_NoFixed_UsesDeployMoment() {
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);
        var embed = EmbedRenderer.Render(Spec(timestamp: true), new DeployResponse(), "app");
        Assert.NotNull(embed.Timestamp);
        Assert.InRange(embed.Timestamp!.Value, before, DateTimeOffset.UtcNow.AddSeconds(2));
    }

    [Fact]
    public void Render_TimestampOn_Fixed_UsesFixedInstant() {
        var fixedAt = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var embed = EmbedRenderer.Render(Spec(timestamp: true, timestampFixed: fixedAt), new DeployResponse(), "app");
        Assert.Equal(fixedAt, embed.Timestamp);
    }

    [Fact]
    public void Render_TimestampOff_FixedIgnored() {
        var fixedAt = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var embed = EmbedRenderer.Render(Spec(timestamp: false, timestampFixed: fixedAt), new DeployResponse(), "app");
        Assert.Null(embed.Timestamp);
    }
}
