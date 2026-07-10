using SyncKit.Bot;
using Xunit;

namespace SyncKit.Bot.Tests;

public class CommandSignatureTests
{
    private static OptionShape Opt(string name, bool required = false, bool autocomplete = false,
        IReadOnlyList<OptionShape>? options = null) =>
        new(name, name + " desc", 3, required, autocomplete, options ?? new List<OptionShape>());

    private static CommandShape Cmd(string name, params OptionShape[] options) =>
        new(name, name + " desc", options);

    [Fact]
    public void Compute_IsOrderInsensitiveByCommandName()
    {
        var a = CommandSignature.Compute(new[] { Cmd("alpha"), Cmd("beta") });
        var b = CommandSignature.Compute(new[] { Cmd("beta"), Cmd("alpha") });
        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_ChangesWhenDescriptionChanges()
    {
        var a = CommandSignature.Compute(new[] { new CommandShape("x", "one", new List<OptionShape>()) });
        var b = CommandSignature.Compute(new[] { new CommandShape("x", "two", new List<OptionShape>()) });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_ChangesWhenOptionAddedOrFlagged()
    {
        var bare = CommandSignature.Compute(new[] { Cmd("x") });
        var withOpt = CommandSignature.Compute(new[] { Cmd("x", Opt("page")) });
        var withRequired = CommandSignature.Compute(new[] { Cmd("x", Opt("page", required: true)) });
        Assert.NotEqual(bare, withOpt);
        Assert.NotEqual(withOpt, withRequired);
    }

    [Fact]
    public void Compute_CapturesNestedOptions()
    {
        var flat = CommandSignature.Compute(new[] { Cmd("x", Opt("sub")) });
        var nested = CommandSignature.Compute(new[]
        {
            Cmd("x", Opt("sub", options: new List<OptionShape> { Opt("name", required: true, autocomplete: true) })),
        });
        Assert.NotEqual(flat, nested);
    }
}
