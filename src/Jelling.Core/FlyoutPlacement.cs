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

    /// <param name="hasTaskbar">Whether a taskbar sits on this monitor; a secondary one may have none.</param>
    public static PixelRect Calculate(
        PixelRect workArea, PixelRect monitorBounds, bool hasTaskbar, int width, int height, double scale)
    {
        var margin = (int)Math.Round(MarginEpx * scale);

        // An auto-hiding taskbar reserves no work area, but still covers the bottom edge when shown
        var bottom = hasTaskbar && workArea == monitorBounds
            ? workArea.Bottom - (int)Math.Round(TaskbarHeightEpx * scale)
            : workArea.Bottom;

        // A work area too small for the flyout keeps its left and top edges on screen; the rest is cut off
        var x = Math.Max(workArea.X, workArea.Right - width - margin);
        var y = Math.Max(workArea.Y + margin, bottom - height - margin);
        var fittedHeight = Math.Max(0, Math.Min(height, bottom - margin - y));

        return new PixelRect(x, y, width, fittedHeight);
    }
}
