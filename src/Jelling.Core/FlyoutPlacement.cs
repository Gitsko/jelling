namespace Jelling.Core;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>
/// Computes where the tray flyout opens, in physical pixels: bottom-right of the work area,
/// like the Windows 11 quick settings and calendar flyouts. A flyout taller than the available
/// space is shortened so it never covers the taskbar; its list scrolls instead.
/// </summary>
public static class FlyoutPlacement
{
    private const int MarginEpx = 12;

    // Windows 11 taskbar height at 100 % scale; only needed when the taskbar auto-hides
    private const int TaskbarHeightEpx = 48;

    public static PixelRect Calculate(PixelRect workArea, PixelRect monitorBounds, int width, int height, double scale)
    {
        var margin = (int)Math.Round(MarginEpx * scale);

        // An auto-hiding taskbar reserves no work area, but still covers the bottom edge when shown
        var bottom = workArea == monitorBounds
            ? workArea.Bottom - (int)Math.Round(TaskbarHeightEpx * scale)
            : workArea.Bottom;

        var x = workArea.Right - width - margin;
        var y = Math.Max(workArea.Y + margin, bottom - height - margin);
        var fittedHeight = Math.Min(height, bottom - margin - y);

        return new PixelRect(x, y, width, fittedHeight);
    }
}
