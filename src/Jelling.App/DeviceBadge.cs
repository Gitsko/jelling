using System.Drawing;
using Jelling.Core;

namespace Jelling.App;

/// <param name="Low">For the level at or below the device's low threshold.</param>
public sealed record DeviceColors(Color Normal, Color Low)
{
    /// <summary>High contrast has one text color for everything, low levels included.</summary>
    public bool IsHighContrast => Low == Normal;
}

/// <summary>
/// What the taskbar shows for one pinned, connected device, in the taskbar element or as a tray icon.
/// Compared by value, so refreshes that change nothing skip the redraw.
/// </summary>
/// <param name="Percent">Null shows only the glyph.</param>
/// <param name="LevelColor">For the number, so a low level can stand out from the glyph.</param>
public sealed record DeviceBadge(Guid Id, string Glyph, int? Percent, Color GlyphColor, Color LevelColor, string ToolTip)
{
    public static IReadOnlyList<DeviceBadge> ForPinned(
        IReadOnlyList<BatteryDevice> devices, DeviceSettings settings, DeviceColors colors) =>
        devices
            .Where(d => d.IsConnected && settings.IsPinned(d.Id))
            .Select(d => For(d, settings, colors))
            .ToList();

    private static DeviceBadge For(BatteryDevice device, DeviceSettings settings, DeviceColors colors)
    {
        // High contrast has no warning color (Low equals Normal); the tooltip names a low level either way
        var look = BadgeContent.For(
            device.BatteryPercent, settings.LowThreshold(device.Id), settings.ShowPercent, hasLowColor: colors.Low != colors.Normal);

        var level = device.BatteryPercent is { } percent ? DeviceText.FormatPercent(percent) : null;
        var toolTip = BadgeContent.ToolTip(device.Name, look.IsLow ? $"{level} ({DeviceText.LowLevelText})" : level);

        return new DeviceBadge(
            device.Id,
            DeviceText.GlyphFor(device.Kind),
            look.ShownPercent,
            look.GlyphIsLow ? colors.Low : colors.Normal,
            look.IsLow ? colors.Low : colors.Normal,
            toolTip);
    }
}
