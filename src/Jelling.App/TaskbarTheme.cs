using System.Drawing;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace Jelling.App;

/// <summary>
/// The colors the app draws into the taskbar with. Compared by value, so a theme event that changes
/// nothing skips the redraw.
/// </summary>
/// <param name="Tray">The app's own tray icon in high contrast; null otherwise, when it shows the app icon.</param>
/// <param name="Devices">The pinned devices, in the taskbar element or as tray icons.</param>
internal sealed record TaskbarTheme(Color? Tray, DeviceColors Devices)
{
    /// <summary>Reads the high-contrast and light/dark settings once for all colors.</summary>
    public static TaskbarTheme Current(UISettings uiSettings)
    {
        // High contrast themes set their own taskbar colors, allow only those and ignore the light/dark setting
        if (new AccessibilitySettings().HighContrast)
        {
            var windowText = uiSettings.UIElementColor(UIElementType.WindowText);
            var text = Color.FromArgb(windowText.R, windowText.G, windowText.B);
            return new TaskbarTheme(text, new DeviceColors(text, text, text));
        }

        // Device icons look like the system's own tray icons: black or white, no brand color. Low levels take
        // the Windows critical colors: dark red on a light taskbar, light red on a dark one, where dark red would
        // be too faint for small digits. The level ring takes Bluetooth Blue, like the app icon; its middle
        // lightness reads on both taskbars
        var light = TaskbarIsLight();
        var blue = Color.FromArgb(0x00, 0x82, 0xFC);
        return light
            ? new TaskbarTheme(null, new DeviceColors(Color.Black, Color.FromArgb(0xC4, 0x2B, 0x1C), blue))
            : new TaskbarTheme(null, new DeviceColors(Color.White, Color.FromArgb(0xFF, 0x99, 0xA4), blue));
    }

    // The taskbar follows the system theme, not the app theme; line glyphs need its contrast color
    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }
}
