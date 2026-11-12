using Microsoft.Win32;

namespace Jelling.App;

/// <summary>
/// Moves a tray icon out of the overflow into the taskbar. Windows 11 has no API for this; Explorer keeps
/// the choice per icon under NotifyIconSettings, the same value the taskbar settings page switches. Only the
/// unpackaged app can do this; in the Store package it does nothing.
/// </summary>
public static class TrayPromotion
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";

    /// <summary>
    /// Promotes the icon unless the user already chose a place for it in the taskbar settings, or the app runs
    /// as the Store package.
    /// </summary>
    /// <returns>False while Explorer has not created the icon's entry yet; true when there is nothing to retry.</returns>
    public static bool TryPromote(Guid iconId)
    {
        // The Store package writes to a virtualized registry Explorer never reads; its users pin the icon themselves
        if (PackageIdentity.IsPackaged)
        {
            return true;
        }

        try
        {
            return Promote(iconId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // The icon then stays in the overflow; not worth retrying
            CrashLog.Write("TrayPromotion", exception);
            return true;
        }
    }

    private static bool Promote(Guid iconId)
    {
        using var settings = Registry.CurrentUser.OpenSubKey(SettingsKey);
        if (settings is null)
        {
            return false;
        }

        var wanted = iconId.ToString("B");
        foreach (var name in settings.GetSubKeyNames())
        {
            using var entry = settings.OpenSubKey(name);
            if (entry?.GetValue("IconGuid") is not string guid || !guid.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A value means the user switched the icon on or off in Settings; that choice wins
            if (entry.GetValue("IsPromoted") is null)
            {
                using var writable = settings.OpenSubKey(name, writable: true);
                writable?.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
            }

            return true;
        }

        return false;
    }
}
