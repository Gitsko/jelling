namespace Jelling.Core;

/// <summary>
/// What a pinned device's badge shows, apart from its colors and glyph.
/// </summary>
public static class BadgeContent
{
    /// <summary>A tray icon's tooltip holds at most 127 characters; H.NotifyIcon throws for longer ones.</summary>
    public const int MaxToolTipLength = 127;

    /// <summary>
    /// Without the number the glyph itself carries the warning color. When there is no separate warning color
    /// (high contrast), a low level keeps its number instead.
    /// </summary>
    public static bool ShowsNumber(bool showPercent, bool isLow, bool hasLowColor) =>
        showPercent || (isLow && !hasLowColor);

    /// <summary>What a pinned device's badge shows for its level.</summary>
    /// <param name="percent">Null when the device reports no level; the badge is then the glyph alone.</param>
    /// <param name="lowThreshold">One of <see cref="BatteryLevel.LowThresholds"/>.</param>
    public static BadgeLook For(int? percent, int lowThreshold, bool showPercent, bool hasLowColor)
    {
        if (percent is not { } level)
        {
            return new BadgeLook(null, IsLow: false, GlyphIsLow: false);
        }

        var isLow = BatteryLevel.IsLow(level, lowThreshold);
        return ShowsNumber(showPercent, isLow, hasLowColor)
            ? new BadgeLook(level, isLow, GlyphIsLow: false)
            : new BadgeLook(null, isLow, GlyphIsLow: isLow);
    }

    /// <summary>"Name – level", shortening the name so the level always fits.</summary>
    /// <param name="level">Null when the device reports none; the tooltip is then the name alone.</param>
    public static string ToolTip(string name, string? level)
    {
        var suffix = level is null ? "" : $" – {level}";
        var room = MaxToolTipLength - suffix.Length;
        if (name.Length > room)
        {
            // Cutting between the halves of a surrogate pair (an emoji in the name) would leave half a character
            var keep = room - 1;
            if (char.IsHighSurrogate(name[keep - 1]))
            {
                keep--;
            }

            name = name[..keep] + "…";
        }

        return name + suffix;
    }
}

/// <param name="ShownPercent">The number drawn next to the glyph; null draws none.</param>
/// <param name="IsLow">Whether the level is at or below the threshold; a shown number then takes the warning color.</param>
/// <param name="GlyphIsLow">Whether the glyph itself takes the warning color, standing in for the number.</param>
public readonly record struct BadgeLook(int? ShownPercent, bool IsLow, bool GlyphIsLow);
