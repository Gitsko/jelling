using Jelling.Core;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;

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
    bool IsLow,
    string PinGlyph,
    string PinLabel,
    int LowThresholdIndex)
{
    private static readonly ResourceLoader Resources = new();

    private static readonly IReadOnlyList<string> LowThresholdLabels = BatteryLevel.LowThresholds
        .Select(threshold => threshold == 0 ? Resources.GetString("LowThresholdOff") : FormatPercent(threshold))
        .ToList();

    private static readonly string LowThresholdToolTipText = Resources.GetString("LowThresholdToolTip");

    // The same for every row; x:Bind inside the item template only reaches instance members
    public IReadOnlyList<string> LowThresholdOptions => LowThresholdLabels;

    public string LowThresholdToolTip => LowThresholdToolTipText;

    public string LowThresholdAutomationName => $"{Name}: {Resources.GetString("LowThreshold")}";

    // Only what describes the device fades when it is disconnected; its settings stay usable
    public double Opacity => IsConnected ? 1.0 : 0.5;

    // The warning glyph replaces the battery glyph, so a low level does not rely on color alone
    public Visibility LevelVisibility => IsLow ? Visibility.Collapsed : Visibility.Visible;

    public Visibility LowVisibility => IsLow ? Visibility.Visible : Visibility.Collapsed;

    // Narrator names a button inside a list row without the row; the device name tells which one
    public string PinAutomationName => $"{Name}: {PinLabel}";

    // Narrator reads list items through ToString; the record default would dump every field.
    // It reads "–" as nothing, and the faded row of a disconnected device says nothing to it either
    public override string ToString()
    {
        var status = Resources.GetString(IsConnected ? "Connected" : "Disconnected");
        var battery = BatteryText == "–" ? Resources.GetString("BatteryUnknown") : BatteryText;
        return IsLow
            ? $"{Name}, {status}, {battery}, {LowLevelText}"
            : $"{Name}, {status}, {battery}";
    }

    /// <param name="lowThreshold">One of <see cref="BatteryLevel.LowThresholds"/>.</param>
    public static DeviceItem From(BatteryDevice device, bool isPinned, int lowThreshold)
    {
        var (batteryGlyph, batteryText) = device.BatteryPercent is { } percent
            ? (GlyphForStep(BatteryLevel.ToStep(percent)), FormatPercent(percent))
            : ("", "–");

        return new DeviceItem(
            device.Id,
            device.Name,
            device.IsConnected,
            GlyphFor(device.Kind),
            batteryGlyph,
            batteryText,
            device.BatteryPercent is { } level && BatteryLevel.IsLow(level, lowThreshold),
            isPinned ? "" : "",
            Resources.GetString(isPinned ? "Unpin" : "Pin"),
            BatteryLevel.LowThresholds.IndexOf(lowThreshold));
    }

    // German puts a space before the percent sign, English does not
    public static string FormatPercent(int percent) => string.Format(Resources.GetString("PercentFormat"), percent);

    public static string LowLevelText => Resources.GetString("LowLevel");

    public static string GlyphFor(DeviceKind kind) => kind switch
    {
        DeviceKind.Headset => "",
        DeviceKind.Mouse => "",
        DeviceKind.Keyboard => "",
        DeviceKind.GameController => "",
        DeviceKind.Speaker => "",
        _ => "",
    };

    // Battery0-Battery9 are E850-E859, Battery10 (full) sits apart at E83F
    private static string GlyphForStep(int step) =>
        step == 10 ? "" : ((char)(0xE850 + step)).ToString();
}
