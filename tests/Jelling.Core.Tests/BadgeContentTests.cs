namespace Jelling.Core.Tests;

public class BadgeContentTests
{
    [Theory]
    [InlineData(LevelDisplay.Number, false, true, true)]
    [InlineData(LevelDisplay.Number, true, false, true)]
    [InlineData(LevelDisplay.Symbol, false, true, false)]
    [InlineData(LevelDisplay.Symbol, true, true, false)]
    [InlineData(LevelDisplay.Symbol, true, false, true)]
    [InlineData(LevelDisplay.Symbol, false, false, false)]
    [InlineData(LevelDisplay.Ring, false, true, false)]
    [InlineData(LevelDisplay.Ring, true, false, false)]
    public void ShowsNumberWhenAskedOrWhenALowSymbolHasNoOwnColor(LevelDisplay display, bool isLow, bool hasLowColor, bool expected)
    {
        Assert.Equal(expected, BadgeContent.ShowsNumber(display, isLow, hasLowColor));
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
        Assert.Equal(new BadgeLook(null, IsLow: false, GlyphIsLow: false), BadgeContent.For(null, 20, LevelDisplay.Number, hasLowColor: true));
    }

    [Fact]
    public void ShownNumberCarriesTheWarning()
    {
        Assert.Equal(new BadgeLook(15, IsLow: true, GlyphIsLow: false), BadgeContent.For(15, 20, LevelDisplay.Number, hasLowColor: true));
    }

    [Fact]
    public void HiddenNumberPassesTheWarningToTheGlyph()
    {
        Assert.Equal(new BadgeLook(null, IsLow: true, GlyphIsLow: true), BadgeContent.For(15, 20, LevelDisplay.Symbol, hasLowColor: true));
    }

    [Fact]
    public void HiddenNumberAboveThresholdShowsAPlainGlyph()
    {
        Assert.Equal(new BadgeLook(null, IsLow: false, GlyphIsLow: false), BadgeContent.For(80, 20, LevelDisplay.Symbol, hasLowColor: true));
    }

    [Fact]
    public void ShownNumberAboveThresholdIsPlain()
    {
        Assert.Equal(new BadgeLook(80, IsLow: false, GlyphIsLow: false), BadgeContent.For(80, 20, LevelDisplay.Number, hasLowColor: true));
    }

    [Fact]
    public void WithoutAWarningColorALevelAboveThresholdStaysHidden()
    {
        Assert.Equal(new BadgeLook(null, IsLow: false, GlyphIsLow: false), BadgeContent.For(80, 20, LevelDisplay.Symbol, hasLowColor: false));
    }

    [Fact]
    public void LowLevelKeepsItsNumberWithoutAWarningColor()
    {
        Assert.Equal(new BadgeLook(15, IsLow: true, GlyphIsLow: false), BadgeContent.For(15, 20, LevelDisplay.Symbol, hasLowColor: false));
    }

    [Fact]
    public void RingShowsTheLevelInsteadOfTheNumber()
    {
        Assert.Equal(new BadgeLook(null, IsLow: false, GlyphIsLow: false, RingPercent: 80), BadgeContent.For(80, 20, LevelDisplay.Ring, hasLowColor: true));
    }

    [Fact]
    public void RingCarriesTheWarningInsteadOfTheGlyph()
    {
        Assert.Equal(new BadgeLook(null, IsLow: true, GlyphIsLow: false, RingPercent: 15), BadgeContent.For(15, 20, LevelDisplay.Ring, hasLowColor: true));
    }

    [Fact]
    public void RingStaysARingWithoutAWarningColorAsItsFillShowsTheLevel()
    {
        Assert.Equal(new BadgeLook(null, IsLow: true, GlyphIsLow: false, RingPercent: 15), BadgeContent.For(15, 20, LevelDisplay.Ring, hasLowColor: false));
    }

    [Fact]
    public void RingWithoutLevelShowsTheGlyphAlone()
    {
        Assert.Equal(new BadgeLook(null, IsLow: false, GlyphIsLow: false), BadgeContent.For(null, 20, LevelDisplay.Ring, hasLowColor: true));
    }
}
