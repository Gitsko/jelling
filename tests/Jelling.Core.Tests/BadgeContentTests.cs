namespace Jelling.Core.Tests;

public class BadgeContentTests
{
    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, false, false)]
    public void ShowsNumberWhenAskedOrWhenLowHasNoOwnColor(bool showPercent, bool isLow, bool hasLowColor, bool expected)
    {
        Assert.Equal(expected, BadgeContent.ShowsNumber(showPercent, isLow, hasLowColor));
    }

    [Fact]
    public void JoinsNameAndLevel()
    {
        Assert.Equal("MX Keys – 80 %", BadgeContent.ToolTip("MX Keys", "80 %"));
    }

    [Fact]
    public void IsTheNameAloneWithoutLevel()
    {
        Assert.Equal("MX Keys", BadgeContent.ToolTip("MX Keys", null));
    }

    [Fact]
    public void ShortensALongNameButKeepsTheLevel()
    {
        var toolTip = BadgeContent.ToolTip(new string('x', 200), "15 % (low)");

        Assert.Equal(BadgeContent.MaxToolTipLength, toolTip.Length);
        Assert.EndsWith("… – 15 % (low)", toolTip);
    }

    [Fact]
    public void ShortensALongNameWithoutLevel()
    {
        var toolTip = BadgeContent.ToolTip(new string('x', 200), null);

        Assert.Equal(BadgeContent.MaxToolTipLength, toolTip.Length);
        Assert.EndsWith("x…", toolTip);
    }

    [Fact]
    public void KeepsANameThatJustFits()
    {
        var name = new string('x', BadgeContent.MaxToolTipLength);

        Assert.Equal(name, BadgeContent.ToolTip(name, null));
    }

    [Fact]
    public void DoesNotCutAnEmojiInHalf()
    {
        var name = new string('x', 125) + "\U0001F3A7" + new string('x', 50);

        Assert.Equal(new string('x', 125) + "…", BadgeContent.ToolTip(name, null));
    }

    [Fact]
    public void WithoutLevelShowsTheGlyphAlone()
    {
        Assert.Equal(new BadgeLook(null, IsLow: false, GlyphIsLow: false), BadgeContent.For(null, 20, showPercent: true, hasLowColor: true));
    }

    [Fact]
    public void ShownNumberCarriesTheWarning()
    {
        Assert.Equal(new BadgeLook(15, IsLow: true, GlyphIsLow: false), BadgeContent.For(15, 20, showPercent: true, hasLowColor: true));
    }

    [Fact]
    public void HiddenNumberPassesTheWarningToTheGlyph()
    {
        Assert.Equal(new BadgeLook(null, IsLow: true, GlyphIsLow: true), BadgeContent.For(15, 20, showPercent: false, hasLowColor: true));
    }

    [Fact]
    public void HiddenNumberAboveThresholdShowsAPlainGlyph()
    {
        Assert.Equal(new BadgeLook(null, IsLow: false, GlyphIsLow: false), BadgeContent.For(80, 20, showPercent: false, hasLowColor: true));
    }

    [Fact]
    public void LowLevelKeepsItsNumberWithoutAWarningColor()
    {
        Assert.Equal(new BadgeLook(15, IsLow: true, GlyphIsLow: false), BadgeContent.For(15, 20, showPercent: false, hasLowColor: false));
    }
}
