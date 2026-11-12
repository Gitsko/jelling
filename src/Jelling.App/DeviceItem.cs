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
    bool CanConnect,
    bool ShowsConnectSlot,
    string KindGlyph,
    string BatteryGlyph,
    string BatteryText,
    bool HasLevel,
    bool IsLow,
    bool InTaskbar,
    bool ShowsWidgetToggle,
    bool OnWidget,
    int LowThresholdIndex)
{
    private static readonly IReadOnlyList<string> LowThresholdLabels = BatteryLevel.LowThresholds
        .Select(threshold => threshold == 0 ? Strings.Get("LowThresholdOff") : DeviceText.FormatPercent(threshold))
        .ToList();

    private static readonly string LowThresholdToolTipText = Strings.Get("LowThresholdToolTip");
    private static readonly string LowThresholdLabel = Strings.Get("LowThreshold");
    private static readonly string TaskbarLabelText = Strings.Get("ShowInTaskbar");
    private static readonly string WidgetLabelText = Strings.Get("ShowOnWidget");
    private static readonly string BatteryLevelFormat = Strings.Get("BatteryLevelFormat");
    private static readonly string BatteryUnknownText = Strings.Get("BatteryUnknown");
    private static readonly string ConnectedText = Strings.Get("Connected");
    private static readonly string ConnectText = Strings.Get("ConnectDevice");
    private static readonly string DisconnectText = Strings.Get("DisconnectDevice");

    // The same for every row; x:Bind inside the item template only reaches instance members
    public IReadOnlyList<string> LowThresholdOptions => LowThresholdLabels;

    public string LowThresholdToolTip => LowThresholdToolTipText;

    public string LowThresholdAutomationName => $"{Name}: {LowThresholdLabel}";

    // Only what describes the device fades when it is disconnected; its settings stay usable
    public double Opacity => IsConnected ? 1.0 : 0.5;

    // The warning glyph replaces the battery glyph, so a low level does not rely on color alone
    public Visibility LevelVisibility => IsLow ? Visibility.Collapsed : Visibility.Visible;

    // For the symbol and the number, which say nothing on their own about being the battery
    public string BatteryToolTip => !HasLevel
        ? BatteryUnknownText
        : IsLow
            ? $"{string.Format(BatteryLevelFormat, BatteryText)}, {DeviceText.LowLevelText}"
            : string.Format(BatteryLevelFormat, BatteryText);

    public Visibility LowVisibility => IsLow ? Visibility.Visible : Visibility.Collapsed;

    // Constant labels: the toggle buttons tell Narrator on or off themselves
    public string TaskbarLabel => TaskbarLabelText;

    public string WidgetLabel => WidgetLabelText;

    // Narrator names a button inside a list row without the row; the device name tells which one
    public string TaskbarAutomationName => $"{Name}: {TaskbarLabel}";

    public string WidgetAutomationName => $"{Name}: {WidgetLabel}";

    public Visibility ConnectVisibility => CanConnect ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ConnectSlotVisibility => ShowsConnectSlot ? Visibility.Visible : Visibility.Collapsed;

    // The tooltip says what a click does; Narrator gets a constant label like the other toggles and adds on or off
    public string ConnectToolTip => IsConnected ? DisconnectText : ConnectText;

    public string ConnectAutomationName => $"{Name}: {ConnectedText}";

    public Visibility WidgetToggleVisibility => ShowsWidgetToggle ? Visibility.Visible : Visibility.Collapsed;

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

    /// <param name="showsConnectSlot">Whether any listed device can connect, so every row keeps room for the button.</param>
    /// <param name="showsWidgetToggle">Whether the widget is on, so the row offers to put the device on it.</param>
    /// <param name="lowThreshold">One of <see cref="BatteryLevel.LowThresholds"/>.</param>
    public static DeviceItem From(
        BatteryDevice device, bool showsConnectSlot, bool inTaskbar, bool showsWidgetToggle, bool onWidget, int lowThreshold)
    {
        var (batteryGlyph, batteryText) = device.BatteryPercent is { } percent
            ? (DeviceText.GlyphForStep(BatteryLevel.ToStep(percent)), DeviceText.FormatPercent(percent))
            : ("", "–");

        return new DeviceItem(
            device.Id,
            device.Name,
            device.IsConnected,
            device.CanConnect,
            showsConnectSlot,
            DeviceText.GlyphFor(device.Kind),
            batteryGlyph,
            batteryText,
            device.BatteryPercent is not null,
            device.BatteryPercent is { } level && BatteryLevel.IsLow(level, lowThreshold),
            inTaskbar,
            showsWidgetToggle,
            onWidget,
            BatteryLevel.LowThresholds.IndexOf(lowThreshold));
    }
}
