using Jelling.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Jelling.App;

/// <summary>
/// Shows the low-battery warnings as Windows notifications.
/// </summary>
public sealed class ToastService : IDisposable
{
    private const string Group = "LowBattery";

    // Long enough to be seen after a meeting, short enough not to outlive the next charge
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private readonly bool _registered;

    /// <param name="onClick">Runs on a thread-pool thread when the user clicks a notification.</param>
    public ToastService(Action onClick)
    {
        // Must be subscribed before Register, otherwise a click starts a second process
        AppNotificationManager.Default.NotificationInvoked += (_, _) => onClick();

        try
        {
            // Unpackaged apps have no manifest to take name and icon from
            var icon = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png"));
            AppNotificationManager.Default.Register("Jelling Battery Monitor", icon);
            _registered = true;
        }
        catch (Exception exception)
        {
            // Without notifications the tray and flyout still show the low level
            CrashLog.Write("Toast", exception);
        }
    }

    public void ShowLow(BatteryDevice device)
    {
        // The notifier only warns about devices that report a level
        if (!_registered || device.BatteryPercent is not { } percent)
        {
            return;
        }

        var notification = new AppNotificationBuilder()
            .AddText(string.Format(Strings.Get("LowBatteryTitle"), device.Name))
            .AddText(string.Format(Strings.Get("LowBatteryText"), DeviceText.FormatPercent(percent)))
            // One toast per device: a later warning replaces the earlier one instead of stacking
            .SetTag(device.Id.ToString("N"))
            .SetGroup(Group)
            .BuildNotification();
        notification.Expiration = DateTimeOffset.Now + Lifetime;
        AppNotificationManager.Default.Show(notification);
    }

    public void Dispose()
    {
        if (_registered)
        {
            AppNotificationManager.Default.Unregister();
        }
    }
}
