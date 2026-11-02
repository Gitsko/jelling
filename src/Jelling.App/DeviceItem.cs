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
        .Select(threshold => threshold == 0 ? Strings.Get("LowThresholdOff") : DeviceText.FormatPercent(threshold))
        .ToList();

    private static readonly string LowThresholdToolTipText = Strings.Get("LowThresholdToolTip");
    private static readonly string LowThresholdLabel = Strings.Get("LowThreshold");

    // The same for every row; x:Bind inside the item template only reaches instance members
    public IReadOnlyList<string> LowThresholdOptions => LowThresholdLabels;

    public string LowThresholdToolTip => LowThresholdToolTipText;

    public string LowThresholdAutomationName => $"{Name}: {LowThresholdLabel}";

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
        var status = Strings.Get(IsConnected ? "Connected" : "Disconnected");
        var battery = HasLevel ? BatteryText : Strings.Get("BatteryUnknown");
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
            isPinned ? "\uE77A" : "\uE718",
            Strings.Get(isPinned ? "Unpin" : "Pin"),
            BatteryLevel.LowThresholds.IndexOf(lowThreshold));
    }
}
