using System.Diagnostics;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Jelling.App;

/// <summary>
/// Starts the app with Windows: the Store package through its StartupTask, the unpackaged app through the
/// per-user Run key, which Windows does not run for packaged apps.
/// </summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Jelling";

    // Declared in Package.appxmanifest
    private const string StartupTaskId = "JellingStartup";

    // Quoted, because the install path may contain spaces
    private static string? Command => Environment.ProcessPath is { } path ? $"\"{path}\"" : null;

    // Only counts when the entry points at this exe; a moved or old build would not start
    public static bool IsEnabled
    {
        get
        {
            if (PackageIdentity.IsPackaged)
            {
                return GetStartupTask().State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string command
                && string.Equals(command, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        if (PackageIdentity.IsPackaged)
        {
            SetStartupTask(enabled);
            return;
        }

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

    private static void SetStartupTask(bool enabled)
    {
        var task = GetStartupTask();
        if (!enabled)
        {
            task.Disable();
            return;
        }

        // Once the user switched it off in Settings, only Settings may switch it on again
        if (Wait(() => task.RequestEnableAsync().AsTask()) == StartupTaskState.DisabledByUser)
        {
            Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
        }
    }

    private static StartupTask GetStartupTask() => Wait(() => StartupTask.GetAsync(StartupTaskId).AsTask());

    // Started and awaited off the STA thread, where waiting can deadlock the cross-process call
    private static T Wait<T>(Func<Task<T>> operation) => Task.Run(operation).GetAwaiter().GetResult();
}
