using System.Drawing;
using Jelling.Core;

namespace Jelling.App;

/// <param name="Low">For the level at or below the device's low threshold.</param>
public sealed record DeviceColors(Color Normal, Color Low);

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
        var glyph = DeviceItem.GlyphFor(device.Kind);
        if (device.BatteryPercent is not { } percent)
        {
            return new DeviceBadge(device.Id, glyph, null, colors.Normal, colors.Normal, device.Name);
        }

        var isLow = BatteryLevel.IsLow(percent, settings.LowThreshold(device.Id));
        var levelColor = isLow ? colors.Low : colors.Normal;
        var toolTip = $"{device.Name} – {DeviceItem.FormatPercent(percent)}";
        if (isLow)
        {
            toolTip += $" ({DeviceItem.LowLevelText})";
        }

        // Without the number the glyph itself carries the warning color. High contrast has no warning color
        // (Low equals Normal), so a low level keeps its number there; the tooltip names it either way
        var showsNumber = settings.ShowPercent || (isLow && colors.Low == colors.Normal);
        return showsNumber
            ? new DeviceBadge(device.Id, glyph, percent, colors.Normal, levelColor, toolTip)
            : new DeviceBadge(device.Id, glyph, null, levelColor, levelColor, toolTip);
    }
}
