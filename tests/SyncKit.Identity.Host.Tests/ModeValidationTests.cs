using Xunit;

namespace SyncKit.Identity.Host.Tests;

public class ModeValidationTests {
    [Theory]
    [InlineData("popup", "popup")]
    [InlineData("inline", "inline")]
    [InlineData("redirect", "redirect")]
    public void ValidateMode_AcceptsKnownValues(string input, string expected) {
        Assert.Equal(expected, Program.ValidateMode(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("POPUP")]
    public void ValidateMode_DefaultsUnknownValuesToPopup(string? input) {
        Assert.Equal("popup", Program.ValidateMode(input));
    }
}
