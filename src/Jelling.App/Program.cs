using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Jelling.App;

/// <summary>
/// Own entry point instead of the XAML-generated one, so a second start (autostart plus a manual
/// launch) hands over to the running instance before it creates a second tray icon.
/// </summary>
public static class Program
{
    private const string InstanceKey = "Jelling";
    private static readonly TimeSpan RedirectTimeout = TimeSpan.FromSeconds(5);

    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!instance.IsCurrent)
        {
            Redirect(instance);
            return;
        }

        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }

    private static void Redirect(AppInstance instance)
    {
        // The running instance sits in the background; only the process the user just started may
        // hand it the foreground, otherwise the flyout opens without focus and never auto-hides
        AllowSetForegroundWindow(instance.ProcessId);

        try
        {
            // Waiting on the STA thread itself can deadlock the cross-process call
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (!Task.Run(() => instance.RedirectActivationToAsync(activation).AsTask()).Wait(RedirectTimeout))
            {
                CrashLog.Write("Redirect", new TimeoutException("The running instance did not take the activation"));
            }
        }
        catch (Exception exception)
        {
            CrashLog.Write("Redirect", exception);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
