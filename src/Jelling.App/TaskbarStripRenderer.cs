using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Jelling.App;

/// <summary>
/// Draws the taskbar element: per device its glyph with the battery level to the right, sized like the
/// taskbar's own icons and clock.
/// </summary>
public static class TaskbarStripRenderer
{
    private const float GlyphSizeEpx = 16;
    private const float TextSizeEpx = 14;
    private const float PaddingEpx = 8;
    private const float GapEpx = 5;

    // ClearType needs an opaque background; on per-pixel alpha it leaves colored fringes.
    // Measuring uses the same hint, as the hint changes the widths
    private const TextRenderingHint Hint = TextRenderingHint.AntiAliasGridFit;

    // Fully transparent pixels let clicks fall through to the taskbar below; this one is invisible but hit-testable
    private static readonly Color Background = Color.FromArgb(1, 0, 0, 0);

    /// <returns>The strip and, in the order of <paramref name="badges"/>, the part of it each badge covers.</returns>
    public static (Bitmap Image, IReadOnlyList<Rectangle> BadgeBounds) Render(
        IReadOnlyList<DeviceBadge> badges, int height, double scale)
    {
        using var glyphFont = new Font(DeviceText.GlyphFont, (float)(GlyphSizeEpx * scale), GraphicsUnit.Pixel);
        using var textFont = new Font(DeviceText.TextFont, (float)(TextSizeEpx * scale), FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center };
        format.FormatFlags |= StringFormatFlags.NoWrap;
        var padding = (float)(PaddingEpx * scale);
        var gap = (float)(GapEpx * scale);

        // Measured and placed in one pass, so the strip's width cannot drift from what is drawn
        var layout = new List<(DeviceBadge Badge, string? Text, RectangleF Glyph, RectangleF Level, Rectangle Bounds)>();
        var x = 0f;
        using (var probe = new Bitmap(1, 1))
        using (var measure = Graphics.FromImage(probe))
        {
            measure.TextRenderingHint = Hint;
            foreach (var badge in badges)
            {
                var start = x;
                x += padding;
                var glyphWidth = measure.MeasureString(badge.Glyph, glyphFont, PointF.Empty, format).Width;
                var glyph = new RectangleF(x, 0, glyphWidth, height);
                x += glyphWidth;

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
                layout.Add((badge, text, glyph, level, bounds));
            }
        }

        var bitmap = new Bitmap(Math.Max(1, (int)MathF.Ceiling(x)), height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Background);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = Hint;

        foreach (var (badge, text, glyph, level, _) in layout)
        {
            using var glyphBrush = new SolidBrush(badge.GlyphColor);
            graphics.DrawString(badge.Glyph, glyphFont, glyphBrush, glyph, format);
            if (text is not null)
            {
                using var textBrush = new SolidBrush(badge.LevelColor);
                graphics.DrawString(text, textFont, textBrush, level, format);
            }
        }

        return (bitmap, layout.Select(item => item.Bounds).ToList());
    }
}
