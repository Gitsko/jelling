using Jelling.Core;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Jelling.App;

/// <summary>
/// How devices and their levels read and look, the same in the flyout, the taskbar, the tray and the toasts.
/// </summary>
internal static class DeviceText
{
    public const string GlyphFont = "Segoe Fluent Icons";
    public const string TextFont = "Segoe UI";

    public static readonly ResourceLoader Resources = new();

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
    public static string GlyphForStep(int step) =>
        step == 10 ? "" : ((char)(0xE850 + step)).ToString();
}
