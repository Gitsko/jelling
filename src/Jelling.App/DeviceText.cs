using Jelling.Core;

namespace Jelling.App;

/// <summary>
/// How devices and their levels read and look, the same in the flyout, the taskbar, the tray and the toasts.
/// </summary>
internal static class DeviceText
{
    public const string GlyphFont = "Segoe Fluent Icons";
    public const string TextFont = "Segoe UI";

    // German puts a space before the percent sign, English does not
    public static string FormatPercent(int percent) => string.Format(Strings.Get("PercentFormat"), percent);

    public static string LowLevelText => Strings.Get("LowLevel");

    public static string GlyphFor(DeviceKind kind) => kind switch
    {
        DeviceKind.Headset => "\uE7F6",
        DeviceKind.Mouse => "\uE962",
        DeviceKind.Keyboard => "\uE765",
        DeviceKind.GameController => "\uE7FC",
        DeviceKind.Speaker => "\uE7F5",
        _ => "\uE702",
    };

    // Battery0-Battery9 are E850-E859, Battery10 (full) sits apart at E83F
    public static string GlyphForStep(int step) =>
        step == 10 ? "\uE83F" : ((char)(0xE850 + step)).ToString();
}
