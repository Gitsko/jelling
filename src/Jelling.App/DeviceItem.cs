using Jelling.Core;
using Microsoft.UI.Xaml;

namespace Jelling.App;

/// <summary>
/// Display form of a <see cref="BatteryDevice"/>: Segoe Fluent glyphs and formatted text.
/// </summary>
public sealed record DeviceItem(
    Guid Id,
    string Name,
    bool IsConnected,
    string KindGlyph,
    string BatteryGlyph,
    string BatteryText,
    bool HasLevel,
    bool IsLow,
    string PinGlyph,
    string PinLabel,
    int LowThresholdIndex)
{
    private static readonly IReadOnlyList<string> LowThresholdLabels = BatteryLevel.LowThresholds
        .Select(threshold => threshold == 0 ? DeviceText.Resources.GetString("LowThresholdOff") : DeviceText.FormatPercent(threshold))
        .ToList();

    private static readonly string LowThresholdToolTipText = DeviceText.Resources.GetString("LowThresholdToolTip");

    // The same for every row; x:Bind inside the item template only reaches instance members
    public IReadOnlyList<string> LowThresholdOptions => LowThresholdLabels;

    public string LowThresholdToolTip => LowThresholdToolTipText;

    public string LowThresholdAutomationName => $"{Name}: {DeviceText.Resources.GetString("LowThreshold")}";

    // Only what describes the device fades when it is disconnected; its settings stay usable
    public double Opacity => IsConnected ? 1.0 : 0.5;

    // The warning glyph replaces the battery glyph, so a low level does not rely on color alone
    public Visibility LevelVisibility => IsLow ? Visibility.Collapsed : Visibility.Visible;

    public Visibility LowVisibility => IsLow ? Visibility.Visible : Visibility.Collapsed;

    // Narrator names a button inside a list row without the row; the device name tells which one
    public string PinAutomationName => $"{Name}: {PinLabel}";

    // Narrator reads list items through ToString; the record default would dump every field.
    // It reads the "–" of a missing level as nothing, and the faded row of a disconnected device says nothing to it either
    public override string ToString()
    {
        var status = DeviceText.Resources.GetString(IsConnected ? "Connected" : "Disconnected");
        var battery = HasLevel ? BatteryText : DeviceText.Resources.GetString("BatteryUnknown");
        return IsLow
            ? $"{Name}, {status}, {battery}, {DeviceText.LowLevelText}"
            : $"{Name}, {status}, {battery}";
    }

    /// <param name="lowThreshold">One of <see cref="BatteryLevel.LowThresholds"/>.</param>
    public static DeviceItem From(BatteryDevice device, bool isPinned, int lowThreshold)
    {
        var (batteryGlyph, batteryText) = device.BatteryPercent is { } percent
            ? (DeviceText.GlyphForStep(BatteryLevel.ToStep(percent)), DeviceText.FormatPercent(percent))
            : ("", "–");

        return new DeviceItem(
            device.Id,
            device.Name,
            device.IsConnected,
            DeviceText.GlyphFor(device.Kind),
            batteryGlyph,
            batteryText,
            device.BatteryPercent is not null,
            device.BatteryPercent is { } level && BatteryLevel.IsLow(level, lowThreshold),
            isPinned ? "\ue77a" : "\ue718",
            DeviceText.Resources.GetString(isPinned ? "Unpin" : "Pin"),
            BatteryLevel.LowThresholds.IndexOf(lowThreshold));
    }
}
