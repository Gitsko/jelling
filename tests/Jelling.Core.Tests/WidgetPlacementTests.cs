namespace Jelling.Core.Tests;

public class WidgetPlacementTests
{
    private static readonly PixelRect Primary = new(0, 0, 2560, 1380);
    private static readonly PixelRect Left = new(-1920, 200, 1920, 1040);

    [Fact]
    public void WithoutStoredPositionSitsTopRightOfThePrimaryWorkArea()
    {
        var position = WidgetPlacement.Place(null, width: 200, height: 48, margin: 24, Primary, [Primary, Left]);

        Assert.Equal(new PixelPoint(2336, 24), position);
    }

    [Fact]
    public void StoredPositionInsideAWorkAreaStays()
    {
        var position = WidgetPlacement.Place(new PixelPoint(-1500, 600), 200, 48, 24, Primary, [Primary, Left]);

        Assert.Equal(new PixelPoint(-1500, 600), position);
    }

    [Fact]
    public void PartlyOffTheEdgeIsPulledBackIn()
    {
        var position = WidgetPlacement.Place(new PixelPoint(2500, 1370), 200, 48, 24, Primary, [Primary]);

        Assert.Equal(new PixelPoint(2360, 1332), position);
    }

    [Fact]
    public void OnAMonitorThatIsGoneMovesToThePrimary()
    {
        var position = WidgetPlacement.Place(new PixelPoint(-1500, 600), 200, 48, 24, Primary, [Primary]);

        Assert.Equal(new PixelPoint(0, 600), position);
    }

    [Fact]
    public void AcrossTwoMonitorsGoesToTheOneHoldingMostOfIt()
    {
        var position = WidgetPlacement.Place(new PixelPoint(-150, 600), 200, 48, 24, Primary, [Primary, Left]);

        Assert.Equal(new PixelPoint(-200, 600), position);
    }

    [Fact]
    public void WiderThanTheWorkAreaAlignsLeft()
    {
        var small = new PixelRect(0, 0, 150, 1000);

        var position = WidgetPlacement.Place(new PixelPoint(50, 100), 200, 48, 24, small, [small]);

        Assert.Equal(new PixelPoint(0, 100), position);
    }
}
