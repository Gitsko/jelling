using System.Drawing;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace Jelling.App;

/// <summary>
/// The colors the app draws into the taskbar and onto the desktop widget with. Compared by value, so a theme event that changes
/// nothing skips the redraw.
/// </summary>
/// <param name="Tray">The app's own tray icon in high contrast; null otherwise, when it shows the app icon.</param>
/// <param name="Devices">The pinned devices, in the taskbar element or as tray icons.</param>
/// <param name="Widget">The pinned devices on the desktop widget, on a plate of their own.</param>
internal sealed record TaskbarTheme(Color? Tray, DeviceColors Devices, DeviceColors Widget, WidgetPlate Plate)
{
    /// <summary>Reads the high-contrast and light/dark settings once for all colors.</summary>
    public static TaskbarTheme Current(UISettings uiSettings)
    {
        // High contrast themes set their own taskbar colors, allow only those and ignore the light/dark setting
        if (new AccessibilitySettings().HighContrast)
        {
            var windowText = uiSettings.UIElementColor(UIElementType.WindowText);
            var text = Color.FromArgb(windowText.R, windowText.G, windowText.B);
            var window = uiSettings.UIElementColor(UIElementType.Window);
            var colors = new DeviceColors(text, text, text);
            return new TaskbarTheme(text, colors, colors, new WidgetPlate(Color.FromArgb(window.R, window.G, window.B), text, Shadow: false));
        }

        // Device icons look like the system's own tray icons: black or white, no brand color. Low levels take
        // the Windows critical colors: dark red on a light background, light red on a dark one, where dark red
        // would be too faint for small digits. The level ring takes Bluetooth Blue, like the app icon; its middle
        // lightness reads on both
        // The widget lies on any wallpaper, so it brings its own plate and takes the app theme, like the
        // system's flyouts; the taskbar follows the system theme
        var devices = ForLightness(IsLight("SystemUsesLightTheme"));
        var appsLight = IsLight("AppsUseLightTheme");

        // The flyouts' solid fallback colors, nearly opaque: a busy photo still shows faintly, but never
        // through the glyphs. The edge keeps a light plate off a light wallpaper
        var plate = appsLight
            ? new WidgetPlate(Color.FromArgb(0xF2, 0xF9, 0xF9, 0xF9), Color.FromArgb(0x1A, 0, 0, 0), Shadow: true)
            : new WidgetPlate(Color.FromArgb(0xF2, 0x2C, 0x2C, 0x2C), Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF), Shadow: true);
        return new TaskbarTheme(null, devices, ForLightness(appsLight), plate);
    }

    private static DeviceColors ForLightness(bool light)
    {
        var blue = Color.FromArgb(0x00, 0x82, 0xFC);
        return light
            ? new DeviceColors(Color.Black, Color.FromArgb(0xC4, 0x2B, 0x1C), blue)
            : new DeviceColors(Color.White, Color.FromArgb(0xFF, 0x99, 0xA4), blue);
    }

    private static bool IsLight(string setting)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue(setting) is int value && value != 0;
    }
}

/// <summary>The surface the desktop widget's devices lie on.</summary>
/// <param name="Shadow">Lifts the plate off the wallpaper; high contrast draws none.</param>
public sealed record WidgetPlate(Color Fill, Color Edge, bool Shadow);
