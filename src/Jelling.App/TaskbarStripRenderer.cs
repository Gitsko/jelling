using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Jelling.App;

/// <summary>
/// Draws the taskbar element: per device its glyph with the battery level to the right or as a ring around it,
/// sized like the taskbar's own icons and clock, on one faint rounded surface that sets the devices apart from the app
/// buttons and the tray.
/// </summary>
public static class TaskbarStripRenderer
{
    private const float GlyphSizeEpx = 16;
    private const float TextSizeEpx = 14;
    private const float PaddingEpx = 8;
    private const float GapEpx = 5;

    // Leaves the strip's surface room around the ring. A heavy ring around a small glyph, like a progress ring,
    // makes the level the first thing to read
    private const float RingSizeEpx = 26;
    private const float RingWidthEpx = 3.5f;
    private const float RingGlyphSizeEpx = 9;

    // The empty part of the ring, faint enough that the filled part reads as the level
    private const int RingTrackAlpha = 0x30;

    // The surface matches the app buttons' hover highlight: 4 epx from top and bottom, Windows 11's corner radius.
    // The side inset keeps it off the strip's edges, so it does not touch an app button's highlight next to it
    private const float SurfaceInsetEpx = 4;
    private const float SurfaceSideInsetEpx = 2;
    private const float SurfaceRadiusEpx = 4;

    // The widget's plate stands free on the wallpaper: Windows' corner radius for free surfaces, and room around it
    // for its shadow. The margin keeps the ring as far off the plate's sides as off its top and bottom
    private const float PlateInsetEpx = 4;
    private const float PlateMarginEpx = 4;
    private const float PlateRadiusEpx = 8;
    private const float PlateShadowEpx = 2;
    private const int PlateShadowAlpha = 0x30;

    // About 6 % white on a dark taskbar, 4 % black on a light one: black shows more, so it gets less
    private const int FillAlphaOnDark = 0x10;
    private const int FillAlphaOnLight = 0x0A;
    private const int OutlineAlphaOnDark = 0x18;
    private const int OutlineAlphaOnLight = 0x10;

    // ClearType needs an opaque background; on per-pixel alpha it leaves colored fringes.
    // Measuring uses the same hint, as the hint changes the widths
    private const TextRenderingHint Hint = TextRenderingHint.AntiAliasGridFit;

    // Fully transparent pixels let clicks fall through to the taskbar below; this one is invisible but hit-testable
    private static readonly Color Background = Color.FromArgb(1, 0, 0, 0);

    /// <param name="colors">The taskbar's device colors; the surface takes the normal one.</param>
    /// <param name="plate">Draws the devices on this plate instead of the taskbar's faint surface.</param>
    /// <returns>The strip and, in the order of <paramref name="badges"/>, the part of it each badge covers.</returns>
    public static (Bitmap Image, IReadOnlyList<Rectangle> BadgeBounds) Render(
        IReadOnlyList<DeviceBadge> badges, DeviceColors colors, int height, double scale, WidgetPlate? plate = null)
    {
        using var glyphFont = new Font(DeviceText.GlyphFont, (float)(GlyphSizeEpx * scale), GraphicsUnit.Pixel);
        using var ringGlyphFont = new Font(DeviceText.GlyphFont, (float)(RingGlyphSizeEpx * scale), GraphicsUnit.Pixel);
        using var textFont = new Font(DeviceText.TextFont, (float)(TextSizeEpx * scale), FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center };
        format.FormatFlags |= StringFormatFlags.NoWrap;
        var padding = (float)(PaddingEpx * scale);
        var gap = (float)(GapEpx * scale);
        var ringSize = (float)(RingSizeEpx * scale);

        // Measured and placed in one pass, so the strip's width cannot drift from what is drawn
        var layout = new List<(DeviceBadge Badge, string? Text, RectangleF Glyph, RectangleF Level, RectangleF Ring, Rectangle Bounds)>();
        var margin = plate is null ? 0 : (float)(PlateMarginEpx * scale);
        var x = margin;
        using (var probe = new Bitmap(1, 1))
        using (var measure = Graphics.FromImage(probe))
        {
            measure.TextRenderingHint = Hint;
            foreach (var badge in badges)
            {
                var start = x;
                x += padding;
                var glyphWidth = measure.MeasureString(badge.Glyph, GlyphFont(badge), PointF.Empty, format).Width;

                // The ring is wider than the glyph; the glyph sits in its middle
                var ring = RectangleF.Empty;
                var column = glyphWidth;
                if (badge.RingPercent is not null)
                {
                    column = MathF.Max(glyphWidth, ringSize);
                    ring = new RectangleF(x + (column - ringSize) / 2, (height - ringSize) / 2, ringSize, ringSize);
                }

                var glyph = new RectangleF(x + (column - glyphWidth) / 2, 0, glyphWidth, height);
                x += column;

                var text = badge.Percent is { } percent ? DeviceText.FormatPercent(percent) : null;
                var level = RectangleF.Empty;
                if (text is not null)
                {
                    x += gap;
                    var textWidth = measure.MeasureString(text, textFont, PointF.Empty, format).Width;

                    // One pixel of slack: the measured width ends at the advance, anti-aliasing reaches past it
                    level = new RectangleF(x, 0, textWidth + 1, height);
                    x += textWidth;
                }

                x += padding;
                var bounds = Rectangle.FromLTRB((int)MathF.Round(start), 0, (int)MathF.Round(x), height);
                layout.Add((badge, text, glyph, level, ring, bounds));
            }
        }

        x += margin;
        var bitmap = new Bitmap(Math.Max(1, (int)MathF.Ceiling(x)), height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Background);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = Hint;

        if (layout.Count > 0 && plate is not null)
        {
            DrawPlate(graphics, bitmap.Width, height, scale, plate);
        }

        // High contrast has no translucent fills, so the surface stays out of it
        else if (layout.Count > 0 && !colors.IsHighContrast)
        {
            DrawSurface(graphics, bitmap.Width, height, scale, colors.Normal);
        }

        foreach (var (badge, text, glyph, level, ring, _) in layout)
        {
            using var glyphBrush = new SolidBrush(badge.GlyphColor);
            if (badge.RingPercent is { } fill)
            {
                DrawRing(graphics, ring, (float)(RingWidthEpx * scale), fill, badge.LevelColor, badge.RingTrack);
                FillCentered(graphics, badge.Glyph, ringGlyphFont, glyphBrush, ring);
            }
            else
            {
                graphics.DrawString(badge.Glyph, glyphFont, glyphBrush, glyph, format);
            }

            if (text is not null)
            {
                using var textBrush = new SolidBrush(badge.LevelColor);
                graphics.DrawString(text, textFont, textBrush, level, format);
            }
        }

        return (bitmap, layout.Select(item => item.Bounds).ToList());

        Font GlyphFont(DeviceBadge badge) => badge.RingPercent is null ? glyphFont : ringGlyphFont;
    }

    // DrawString places a glyph by its advance and the font's line box, a pixel or so off its ink's middle. Inside a
    // ring that shows, so the outline's own bounds go on the ring's center
    private static void FillCentered(Graphics graphics, string glyph, Font font, Brush brush, RectangleF ring)
    {
        using var path = new GraphicsPath();
        path.AddString(glyph, font.FontFamily, (int)font.Style, font.Size, PointF.Empty, StringFormat.GenericTypographic);
        var ink = path.GetBounds();
        using var move = new Matrix();
        move.Translate(ring.X + (ring.Width - ink.Width) / 2 - ink.X, ring.Y + (ring.Height - ink.Height) / 2 - ink.Y);
        path.Transform(move);
        graphics.FillPath(brush, path);
    }

    /// <summary>A ring filled clockwise from the top in proportion to the level, on a faint full ring.</summary>
    /// <param name="bounds">The ring's outer edge.</param>
    /// <param name="track">The color the empty part takes, faded; null leaves it out (high contrast has no translucency).</param>
    internal static void DrawRing(Graphics graphics, RectangleF bounds, float width, int percent, Color color, Color? track)
    {
        // The pen is centered on the path; inset by half its width the ring stays inside the bounds
        var path = RectangleF.Inflate(bounds, -width / 2, -width / 2);
        if (track is { } empty)
        {
            using var trackPen = new Pen(Color.FromArgb(RingTrackAlpha, empty), width);
            graphics.DrawEllipse(trackPen, path);
        }

        if (percent <= 0)
        {
            return;
        }

        using var pen = new Pen(color, width);
        if (percent >= 100)
        {
            graphics.DrawEllipse(pen, path);
            return;
        }

        // Round ends like a progress ring; the track keeps flat ones, it has none
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        graphics.DrawArc(pen, path, -90, 360f * percent / 100);
    }

    private static void DrawPlate(Graphics graphics, int width, int height, double scale, WidgetPlate plate)
    {
        // Whole pixels and half-pixel offsets keep the one-pixel edge sharp, as on the surface
        var inset = MathF.Round((float)(PlateInsetEpx * scale));
        var surface = new RectangleF(inset + 0.5f, inset + 0.5f, width - 2 * inset - 1, height - 2 * inset - 1);
        if (surface.Width <= 0 || surface.Height <= 0)
        {
            return;
        }

        var radius = (float)(PlateRadiusEpx * scale);
        if (plate.Shadow)
        {
            // Rings of fading black around the plate, a pixel each: a soft shadow without a blur pass. Only outside
            // the plate, which is not fully opaque and would show a shadow beneath it as a gray cast
            var reach = Math.Max(1, (int)MathF.Round((float)(PlateShadowEpx * scale)));
            for (var step = 1; step <= reach; step++)
            {
                var fade = 1 - (float)step / (reach + 1);
                using var ring = new Pen(Color.FromArgb((int)(PlateShadowAlpha * fade * fade), Color.Black), 1);
                var outer = RectangleF.Inflate(surface, step, step);
                var corner = new SizeF(2 * (radius + step), 2 * (radius + step));
                graphics.DrawRoundedRectangle(ring, outer, corner);
            }
        }

        var size = new SizeF(2 * radius, 2 * radius);
        using var fill = new SolidBrush(plate.Fill);
        using var edge = new Pen(plate.Edge, 1);
        graphics.FillRoundedRectangle(fill, surface, size);
        graphics.DrawRoundedRectangle(edge, surface, size);
    }

    // Tinted with the device color, white on a dark taskbar and black on a light one, like the taskbar's own fills.
    // A low battery's color would tint it red, so it is not taken from a badge
    private static void DrawSurface(Graphics graphics, int width, int height, double scale, Color tint)
    {
        var darkTint = tint.GetBrightness() < 0.5f;

        // Whole pixels, so the outline lands on the same grid at every scale
        var inset = MathF.Round((float)(SurfaceInsetEpx * scale));
        var side = MathF.Round((float)(SurfaceSideInsetEpx * scale));
        var radius = (float)(SurfaceRadiusEpx * scale);

        // Half-pixel offsets keep the one-pixel outline sharp
        var surface = new RectangleF(side + 0.5f, inset + 0.5f, width - 2 * side - 1, height - 2 * inset - 1);
        if (surface.Width <= 0 || surface.Height <= 0)
        {
            return;
        }

        using var fill = new SolidBrush(Color.FromArgb(darkTint ? FillAlphaOnLight : FillAlphaOnDark, tint));
        using var outline = new Pen(Color.FromArgb(darkTint ? OutlineAlphaOnLight : OutlineAlphaOnDark, tint), 1);
        var corner = new SizeF(2 * radius, 2 * radius);
        graphics.FillRoundedRectangle(fill, surface, corner);
        graphics.DrawRoundedRectangle(outline, surface, corner);
    }
}
