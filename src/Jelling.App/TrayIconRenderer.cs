using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Jelling.App;

/// <summary>
/// Draws the tray icons: a Segoe Fluent glyph, for device icons with the battery level below. H.NotifyIcon's GeneratedIconSource does not resolve
/// icon fonts and renders an empty bitmap for them, so the glyph is drawn with GDI+ directly.
/// </summary>
public static class TrayIconRenderer
{
    private const int Size = 32;
    private const float GlyphHeight = 18;
    private const float RingWidth = 2.5f;

    // The font's strokes are hairlines at tray size; tracing the outline thickens them
    private const float GlyphBoldWidth = 1.5f;

    // Device icons stack the glyph above the level; side by side at 16 px the glyph would be only 3 px wide
    private const float DigitHeightRatio = 7f / 16;
    private const float GapRatio = 1f / 16;

    private const float SeparatorWidthRatio = 1f / 16;
    private const float SeparatorMarginRatio = 1f / 16;
    private const int SeparatorAlpha = 0x60;

    private const int SmCxSmIcon = 49;

    public static Icon Render(string glyph, Color color) =>
        ToIcon(Size, graphics =>
        {
            using var ring = new Pen(color, RingWidth);
            graphics.DrawEllipse(ring, Ring(RingWidth));
            DrawGlyph(graphics, glyph, GlyphHeight, color);
        });

    /// <summary>
    /// A device glyph with its battery level below, drawn at the tray's real icon size so the digits stay sharp.
    /// </summary>
    /// <param name="percent">The digits below the glyph; null draws none.</param>
    /// <param name="levelColor">For the digits or the ring, so a low level can stand out from the glyph.</param>
    /// <param name="ringPercent">The level a ring around the glyph is filled to; null draws no ring.</param>
    /// <param name="track">The color the ring's empty part takes, faded; null leaves it out.</param>
    /// <param name="size">From <see cref="SmallIconSize"/>.</param>
    public static Icon RenderDevice(string glyph, int? percent, Color color, Color levelColor, int? ringPercent, Color? track, int size) =>
        ToIcon(size, graphics =>
        {
            using var glyphBrush = new SolidBrush(color);
            if (ringPercent is { } fill)
            {
                // Same proportions as the app's own tray icon: the ring at the edge, the glyph inside it
                TaskbarStripRenderer.DrawRing(graphics, new RectangleF(0, 0, size, size), MathF.Max(1, size * RingWidth / Size), fill, levelColor, track);
                using var inner = Outline(glyph, DeviceText.GlyphFont, FontStyle.Regular, size * GlyphHeight / Size, size * GlyphHeight / Size);
                Place(inner, size, size, top: 0);
                graphics.FillPath(glyphBrush, inner);
                return;
            }

            if (percent is not { } level)
            {
                using var alone = Outline(glyph, DeviceText.GlyphFont, FontStyle.Regular, size * 0.75f, size);
                Place(alone, size, size, top: 0);
                graphics.FillPath(glyphBrush, alone);
                return;
            }

            // Whole pixels keep the digits' horizontal strokes on the pixel grid
            var digitHeight = MathF.Round(size * DigitHeightRatio);
            var glyphHeight = size - digitHeight - MathF.Max(1, MathF.Round(size * GapRatio)) - 1;

            using var digits = Outline(level.ToString(CultureInfo.InvariantCulture), DeviceText.TextFont, FontStyle.Bold, digitHeight, size);
            using var symbol = Outline(glyph, DeviceText.GlyphFont, FontStyle.Regular, glyphHeight, size - 2);
            using var digitBrush = new SolidBrush(levelColor);
            Place(symbol, size, glyphHeight, top: 0);
            Place(digits, size, digitHeight, top: size - digitHeight);
            graphics.FillPath(glyphBrush, symbol);
            graphics.FillPath(digitBrush, digits);
        });

    /// <summary>
    /// A thin vertical line, centered, that sets the device icons apart from the system icons after them.
    /// </summary>
    /// <param name="color">The device glyph color.</param>
    /// <param name="faded">Draws the line translucent, so it stays quieter than the icons.</param>
    /// <param name="size">From <see cref="SmallIconSize"/>.</param>
    public static Icon RenderSeparator(Color color, bool faded, int size) =>
        ToIcon(size, graphics =>
        {
            // Whole pixels without anti-aliasing keep the line sharp instead of a blurred two-pixel band.
            // The same margin above and below keeps it vertically centered at every size
            graphics.SmoothingMode = SmoothingMode.None;
            var width = Math.Max(1, (int)MathF.Round(size * SeparatorWidthRatio));
            var height = size - 2 * Math.Max(1, (int)MathF.Round(size * SeparatorMarginRatio));
            using var brush = new SolidBrush(faded ? Color.FromArgb(SeparatorAlpha, color) : color);
            graphics.FillRectangle(brush, (size - width) / 2, (size - height) / 2, width, height);
        });

    private static RectangleF Ring(float width)
    {
        var inset = width / 2;
        return new RectangleF(inset, inset, Size - width, Size - width);
    }

    // Fitting into the icon's width keeps wide glyphs like the keyboard inside the ring. DrawString pads for
    // overhanging characters and pushes the glyph off-center; the outline's real bounds center it to the nearest pixel
    private static void DrawGlyph(Graphics graphics, string glyph, float height, Color color)
    {
        using var path = Outline(glyph, DeviceText.GlyphFont, FontStyle.Regular, height, Size);
        Place(path, Size, Size, top: 0);
        using var brush = new SolidBrush(color);
        using var bold = new Pen(color, GlyphBoldWidth) { LineJoin = LineJoin.Round };
        graphics.FillPath(brush, path);
        graphics.DrawPath(bold, path);
    }

    // The text's ink scaled to the given height (or narrower to fit maxWidth), with its top left corner at 0,0
    private static GraphicsPath Outline(string text, string family, FontStyle style, float height, float maxWidth)
    {
        using var fontFamily = new FontFamily(family);
        var path = new GraphicsPath();
        path.AddString(text, fontFamily, (int)style, 100, PointF.Empty, StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        var scale = Math.Min(height / bounds.Height, maxWidth / bounds.Width);
        using var fit = new Matrix();
        fit.Scale(scale, scale);
        fit.Translate(-bounds.X, -bounds.Y);
        path.Transform(fit);
        return path;
    }

    // Centers the outline horizontally in a row starting at top; whole-pixel offsets avoid blurring it
    private static void Place(GraphicsPath path, int size, float rowHeight, float top)
    {
        var bounds = path.GetBounds();
        using var move = new Matrix();
        move.Translate(MathF.Round((size - bounds.Width) / 2), MathF.Round(top + (rowHeight - bounds.Height) / 2));
        path.Transform(move);
    }

    /// <summary>
    /// The size Explorer shows tray icons at; a larger image is scaled down and its digits blur. It follows the
    /// taskbar's DPI, which the system DPI misses after a scale change without signing out.
    /// </summary>
    public static int SmallIconSize()
    {
        var dpi = NativeMethods.TaskbarDpi();
        return GetSystemMetricsForDpi(SmCxSmIcon, dpi != 0 ? dpi : GetDpiForSystem());
    }

    private static Icon ToIcon(int size, Action<Graphics> paint)
    {
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            paint(graphics);
        }

        var handle = bitmap.GetHicon();
        try
        {
            // FromHandle does not own the handle; the clone does, so the native icon can be freed here
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
