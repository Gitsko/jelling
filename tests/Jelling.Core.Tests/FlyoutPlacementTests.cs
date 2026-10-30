namespace Jelling.Core.Tests;

public class FlyoutPlacementTests
{
    [Fact]
    public void PlacesBottomRightAboveTaskbar()
    {
        var workArea = new PixelRect(0, 0, 1920, 1032);
        var monitor = new PixelRect(0, 0, 1920, 1080);

        var bounds = FlyoutPlacement.Calculate(workArea, monitor, width: 360, height: 300, scale: 1.0);

        Assert.Equal(new PixelRect(1548, 720, 360, 300), bounds);
    }

    [Fact]
    public void ReservesTaskbarSpaceWhenTaskbarAutoHides()
    {
        var screen = new PixelRect(0, 0, 1920, 1080);

        var bounds = FlyoutPlacement.Calculate(screen, screen, width: 360, height: 300, scale: 1.0);

        Assert.Equal(new PixelRect(1548, 720, 360, 300), bounds);
    }

    [Fact]
    public void ScalesMarginOnSecondaryMonitor()
    {
        var workArea = new PixelRect(1920, 0, 2560, 1380);
        var monitor = new PixelRect(1920, 0, 2560, 1440);

        var bounds = FlyoutPlacement.Calculate(workArea, monitor, width: 540, height: 450, scale: 1.5);

        Assert.Equal(new PixelRect(3922, 912, 540, 450), bounds);
    }

    [Fact]
    public void ShrinksToWorkAreaWhenFlyoutIsTallerThanSpace()
    {
        var workArea = new PixelRect(0, 0, 1280, 672);
        var monitor = new PixelRect(0, 0, 1280, 720);

        var bounds = FlyoutPlacement.Calculate(workArea, monitor, width: 360, height: 900, scale: 1.0);

        Assert.Equal(new PixelRect(908, 12, 360, 648), bounds);
    }
}
