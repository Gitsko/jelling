namespace Jelling.Core.Tests;

public class BatteryLevelTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(40, 4)]
    [InlineData(96, 10)]
    [InlineData(100, 10)]
    public void MapsPercentToTenSteps(int percent, int expectedStep)
    {
        Assert.Equal(expectedStep, BatteryLevel.ToStep(percent));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void RejectsOutOfRangePercent(int percent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BatteryLevel.ToStep(percent));
    }

    [Theory]
    [InlineData(0, 20, true)]
    [InlineData(20, 20, true)]
    [InlineData(21, 20, false)]
    [InlineData(100, 20, false)]
    [InlineData(40, 40, true)]
    [InlineData(41, 40, false)]
    public void TreatsTheThresholdAndBelowAsLow(int percent, int threshold, bool expected)
    {
        Assert.Equal(expected, BatteryLevel.IsLow(percent, threshold));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ThresholdZeroIsNeverLow(int percent)
    {
        Assert.False(BatteryLevel.IsLow(percent, 0));
    }

    [Fact]
    public void OffersTheDefaultThreshold()
    {
        Assert.Contains(BatteryLevel.DefaultLowThreshold, BatteryLevel.LowThresholds);
    }
}
