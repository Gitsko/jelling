namespace Jelling.Core;

public readonly record struct PixelPoint(int X, int Y);

/// <summary>
/// Computes where the desktop widget sits, in physical screen pixels: where the user dropped it, kept
/// fully inside one monitor's work area, so neither a monitor that went away nor a drop half off the
/// edge loses it.
/// </summary>
public static class WidgetPlacement
{
    /// <param name="stored">Where the user left it; null until it was first moved.</param>
    /// <param name="margin">The distance from the primary work area's top right corner without a stored position.</param>
    /// <param name="workAreas">The work areas of all monitors, the primary one included.</param>
    public static PixelPoint Place(
        PixelPoint? stored, int width, int height, int margin, PixelRect primaryWorkArea, IReadOnlyList<PixelRect> workAreas)
    {
        if (stored is not { } position)
        {
            return Clamp(new PixelPoint(primaryWorkArea.Right - width - margin, primaryWorkArea.Y + margin), width, height, primaryWorkArea);
        }

        var widget = new PixelRect(position.X, position.Y, width, height);
        var best = primaryWorkArea;
        var bestOverlap = 0L;
        foreach (var area in workAreas)
        {
            var overlap = Overlap(widget, area);
            if (overlap > bestOverlap)
            {
                best = area;
                bestOverlap = overlap;
            }
        }

        return Clamp(position, width, height, best);
    }

    // Too wide or too tall for the area, it keeps its left or top edge inside
    private static PixelPoint Clamp(PixelPoint position, int width, int height, PixelRect area) =>
        new(Math.Max(area.X, Math.Min(position.X, area.Right - width)),
            Math.Max(area.Y, Math.Min(position.Y, area.Bottom - height)));

    private static long Overlap(PixelRect a, PixelRect b)
    {
        var width = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        var height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return width > 0 && height > 0 ? (long)width * height : 0;
    }
}
