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

    /// <summary>"Name – level", shortening the name so the level always fits.</summary>
    /// <param name="level">Null when the device reports none; the tooltip is then the name alone.</param>
    public static string ToolTip(string name, string? level)
    {
        var suffix = level is null ? "" : $" – {level}";
        var room = MaxToolTipLength - suffix.Length;
        if (name.Length > room)
        {
            name = name[..(room - 1)] + "…";
        }

        return name + suffix;
    }
}
