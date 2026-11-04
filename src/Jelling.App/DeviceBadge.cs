using System.Drawing;
using Jelling.Core;

namespace Jelling.App;

/// <param name="Low">For the level at or below the device's low threshold.</param>
/// <param name="Ring">For the level ring while the level is not low.</param>
public sealed record DeviceColors(Color Normal, Color Low, Color Ring)
{
    /// <summary>High contrast has one text color for everything, low levels included.</summary>
    public bool IsHighContrast => Low == Normal;
}

/// <summary>
/// What the taskbar or the widget shows for one connected device: in the taskbar element, as a tray icon or on the widget.
/// Compared by value, so refreshes that change nothing skip the redraw.
/// </summary>
/// <param name="Percent">The number next to the glyph; null shows none.</param>
/// <param name="LevelColor">For the number or the ring, so a low level can stand out from the glyph.</param>
/// <param name="RingPercent">The level the ring around the glyph is filled to; null draws no ring.</param>
/// <param name="RingTrack">The color the ring's empty part takes, faded; null leaves it out.</param>
public sealed record DeviceBadge(
    Guid Id, string Glyph, int? Percent, Color GlyphColor, Color LevelColor, string ToolTip,
    int? RingPercent = null, Color? RingTrack = null)
{
    /// <summary>The connected devices the user put in the taskbar.</summary>
    public static IReadOnlyList<DeviceBadge> ForTaskbar(
        IReadOnlyList<BatteryDevice> devices, DeviceSettings settings, DeviceColors colors) =>
        ForShown(devices, settings.IsPinned, settings, colors);

    /// <summary>The connected devices the user put on the widget.</summary>
    public static IReadOnlyList<DeviceBadge> ForWidget(
        IReadOnlyList<BatteryDevice> devices, DeviceSettings settings, DeviceColors colors) =>
        ForShown(devices, settings.IsOnWidget, settings, colors);

    private static List<DeviceBadge> ForShown(
        IReadOnlyList<BatteryDevice> devices, Func<Guid, bool> isShown, DeviceSettings settings, DeviceColors colors) =>
        devices
            .Where(d => d.IsConnected && isShown(d.Id))
            .Select(d => For(d, settings, colors))
            .ToList();

    private static DeviceBadge For(BatteryDevice device, DeviceSettings settings, DeviceColors colors)
    {
        // High contrast has no warning color (Low equals Normal); the tooltip names a low level either way
        var look = BadgeContent.For(
            device.BatteryPercent, settings.LowThreshold(device.Id), settings.LevelDisplay, hasLowColor: colors.Low != colors.Normal);

        var level = device.BatteryPercent is { } percent ? DeviceText.FormatPercent(percent) : null;
        var toolTip = BadgeContent.ToolTip(device.Name, look.IsLow ? $"{level} ({DeviceText.LowLevelText})" : level);

        return new DeviceBadge(
            device.Id,
            DeviceText.GlyphFor(device.Kind),
            look.ShownPercent,
            look.GlyphIsLow ? colors.Low : colors.Normal,
            look.IsLow ? colors.Low : look.RingPercent is null ? colors.Normal : colors.Ring,
            toolTip,
            look.RingPercent,
            // High contrast has no translucent colors; the ring's fill alone shows the level there
            colors.IsHighContrast ? null : colors.Normal);
    }
}
