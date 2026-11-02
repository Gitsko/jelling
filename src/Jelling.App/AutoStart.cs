using Microsoft.Win32;

namespace Jelling.App;

/// <summary>
/// Starts the app with Windows through the per-user Run key; an unpackaged app has no StartupTask.
/// </summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Jelling";

    // Quoted, because the install path may contain spaces
    private static string? Command => Environment.ProcessPath is { } path ? $"\"{path}\"" : null;

    // Only counts when the entry points at this exe; a moved or old build would not start
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string command
                && string.Equals(command, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            // An empty entry would start nothing and still show the menu item as ticked
            key.SetValue(ValueName, Command ?? throw new InvalidOperationException("The path of the running exe is unknown"));
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
