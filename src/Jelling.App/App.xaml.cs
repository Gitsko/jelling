using Jelling.Core;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.UI.ViewManagement;

namespace Jelling.App;

public partial class App : Application
{
    private const string TrayGlyph = "\uE702";

    // At autostart Explorer may not take tray icons yet; without the icon the app cannot be reached
    private static readonly TimeSpan TrayIconRetryDelay = TimeSpan.FromSeconds(5);

    private readonly IBatteryProvider _provider = new PnpBatteryProvider();
    private readonly RefreshCoalescer _refresh;
    private readonly DeviceSettings _settings = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jelling", "settings.json"));
    private readonly LowBatteryNotifier _lowBattery = new();

    // Held in a field: the event stops firing once the instance is collected
    private readonly UISettings _uiSettings = new();

    private TaskbarIcon? _trayIcon;
    private FlyoutWindow? _flyout;
    private DeviceChangeWatcher? _watcher;
    private DeviceTrayIcons? _deviceIcons;
    private TaskbarElement? _taskbarElement;
    private ToastService? _toasts;
    private UpdateChecker? _updates;
    private IReadOnlyList<BatteryDevice> _devices = [];
    private TaskbarTheme _theme = null!;
    private DispatcherQueueTimer? _trayIconRetry;
    private bool _trayIconFailureLogged;

    // Callbacks already queued on the dispatcher still run after Shutdown; they must not re-create icons
    private bool _shuttingDown;

    public App()
    {
        InitializeComponent();
        _refresh = new RefreshCoalescer(RefreshCoreAsync);

        // The app lives in the tray; closing or hiding the flyout must not end the process
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

        UnhandledException += (_, e) => CrashLog.Write("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => CrashLog.Write("Task", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Watcher, activation, theme and toast events arrive on thread-pool threads;
        // the coalescer, the list and the icons live on the UI thread
        var dispatcher = DispatcherQueue.GetForCurrentThread();

        // A second start was redirected here by Program.Main; open the flyout instead of a second tray icon.
        // Subscribed first, so a start that arrives while the rest is set up is not lost
        AppInstance.GetCurrent().Activated += (_, _) => dispatcher.TryEnqueue(OpenFlyout);

        _flyout = new FlyoutWindow();
        _flyout.PinToggled += (_, deviceId) => TogglePin(deviceId);
        _flyout.LowThresholdChanged += (_, change) => SetLowThreshold(change.DeviceId, change.Threshold);
        _flyout.RowsOutdated += (_, _) => ShowLastDevices();
        _deviceIcons = new DeviceTrayIcons(ToggleFlyout);
        _taskbarElement = new TaskbarElement(ToggleFlyout);
        _taskbarElement.AvailabilityChanged += (_, _) => ShowInTaskbarAfterRecheck();
        _toasts = new ToastService(() => dispatcher.TryEnqueue(OpenFlyout));
        _updates = new UpdateChecker();
        _updates.UpdateAvailable += (_, latest) => _flyout.ShowUpdate(latest);
        _theme = TaskbarTheme.Current(_uiSettings);
        _ = RefreshAsync();

        _watcher = new DeviceChangeWatcher();
        _watcher.Changed += (_, _) => dispatcher.TryEnqueue(() => _ = RefreshAsync());
        _watcher.Start();

        var title = DeviceText.Resources.GetString("FlyoutHeader/Text");
        var menu = TrayMenu.Create(title, () => _settings.ShowPercent, SetShowPercent, Shutdown, Guard);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = title,
            Icon = TrayIconRenderer.Render(TrayGlyph, _theme.Tray),
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(ToggleFlyout),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
        };

        CreateTrayIcon();

        // Raised for theme, accent and high-contrast changes, often several times per change
        _uiSettings.ColorValuesChanged += (_, _) => dispatcher.TryEnqueue(ApplyTheme);

        // A toast clicked after the app had ended starts it again; NotificationInvoked only covers a running app
        if (AppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.AppNotification)
        {
            OpenFlyout();
        }
    }

    private void CreateTrayIcon()
    {
        if (_shuttingDown || _trayIcon is null)
        {
            return;
        }

        try
        {
            // false: keep the process out of Efficiency Mode so the flyout opens without delay
            _trayIcon.ForceCreate(false);
        }
        catch (Exception exception)
        {
            // A tray that never accepts the icon would otherwise be logged every few seconds
            if (!_trayIconFailureLogged)
            {
                CrashLog.Write("TrayIcon", exception);
                _trayIconFailureLogged = true;
            }

            if (_trayIconRetry is null)
            {
                _trayIconRetry = DispatcherQueue.GetForCurrentThread().CreateTimer();
                _trayIconRetry.Interval = TrayIconRetryDelay;
                _trayIconRetry.IsRepeating = false;
                _trayIconRetry.Tick += (_, _) => CreateTrayIcon();
            }

            _trayIconRetry.Start();
        }
    }

    // The icons keep their old color when this fails
    private void ApplyTheme() => Guard("Theme", () =>
    {
        if (_shuttingDown || _trayIcon is null)
        {
            return;
        }

        var theme = TaskbarTheme.Current(_uiSettings);
        if (theme == _theme)
        {
            return;
        }

        _theme = theme;

        // TaskbarIcon does not own the image it shows
        var previous = _trayIcon.Icon;
        _trayIcon.Icon = TrayIconRenderer.Render(TrayGlyph, theme.Tray);
        previous?.Dispose();

        // Device icons re-render because their look includes the color; the flyout rows follow
        // the theme by themselves, and rebuilding them would close an open threshold list
        ShowInTaskbar();
    });

    private void ToggleFlyout()
    {
        if (_shuttingDown)
        {
            return;
        }

        _flyout?.Toggle();

        // Querying Windows takes a few hundred ms; show the last list at once and update it in place
        if (_flyout?.AppWindow.IsVisible == true)
        {
            _ = RefreshAsync();
        }
    }

    private void OpenFlyout()
    {
        if (_shuttingDown)
        {
            return;
        }

        _flyout?.Open();
        _ = RefreshAsync();
    }

    private Task RefreshAsync() => _refresh.Request();

    private async Task RefreshCoreAsync()
    {
        try
        {
            _devices = await _provider.GetDevicesAsync();
            if (_shuttingDown)
            {
                return;
            }

            ShowDevices();
            WarnLow();
        }
        catch (Exception exception)
        {
            // Keep showing the last known list; a failed query must not end the tray app
            CrashLog.Write("Refresh", exception);
        }
    }

    private void ShowDevices()
    {
        if (_shuttingDown)
        {
            return;
        }

        _flyout?.SetDevices(_devices
            .Select(d => DeviceItem.From(d, _settings.IsPinned(d.Id), _settings.LowThreshold(d.Id)))
            .ToList());
        ShowInTaskbar();
    }

    // The tray icons stand in while the taskbar has no room for the element or Explorer is restarting
    private void ShowInTaskbar()
    {
        if (_shuttingDown)
        {
            return;
        }

        var badges = DeviceBadge.ForPinned(_devices, _settings, _theme.Devices);
        var inElement = _taskbarElement?.Show(badges) == true;
        _deviceIcons?.Update(inElement ? [] : badges);
    }

    // Raised from the element's timer
    private void ShowInTaskbarAfterRecheck() => Guard("Redraw", ShowInTaskbar);

    private void WarnLow()
    {
        // One failed toast must not cost the other devices theirs; the tray and flyout still show it
        foreach (var device in _lowBattery.Check(_devices, _settings.LowThreshold))
        {
            Guard("Toast", () => _toasts?.ShowLow(device));
        }
    }

    private void TogglePin(Guid deviceId)
    {
        Save(() => _settings.TogglePin(deviceId));

        // From the last list: the pin is a local change, there is nothing new to ask Windows for
        ShowLastDevices();
    }

    // Raised from the flyout
    private void ShowLastDevices() => Guard("Redraw", ShowDevices);

    // Raised from the menu
    private void SetShowPercent(bool show)
    {
        Save(() => _settings.SetShowPercent(show));
        Guard("Redraw", ShowInTaskbar);
    }

    private void SetLowThreshold(Guid deviceId, int threshold)
    {
        // A recycled row selects its new device's value; that is no choice and must not pin the default
        if (_settings.LowThreshold(deviceId) == threshold)
        {
            return;
        }

        Save(() => _settings.SetLowThreshold(deviceId, threshold));

        // Raised from the flyout
        Guard("LowThreshold", () =>
        {
            // The flyout rebuilds its rows itself once the list has closed; here they would close it under the user
            ShowInTaskbar();

            // A warning at the old threshold must not swallow the first one at the new threshold;
            // a threshold raised above the current level warns at once
            _lowBattery.Reset(deviceId);
            WarnLow();
        });
    }

    // The setting still applies until the app ends; only saving it failed
    private static void Save(Action change)
    {
        try
        {
            change();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("Settings", exception);
        }
    }

    // For work raised from menus, the flyout, timers and system events: a failure must not end the tray app
    private static void Guard(string source, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            CrashLog.Write(source, exception);
        }
    }

    // Each step on its own: one that fails must neither skip the rest nor keep the process alive
    // without its instance key, where the next start would open a second instance beside it
    private void Shutdown()
    {
        _shuttingDown = true;
        try
        {
            Guard("Shutdown", () => _trayIconRetry?.Stop());

            // A start during shutdown would otherwise be redirected to this dying instance and get lost
            Guard("Shutdown", () => AppInstance.GetCurrent().UnregisterKey());
            Guard("Shutdown", () => _watcher?.Dispose());
            Guard("Shutdown", () => _toasts?.Dispose());
            Guard("Shutdown", () => _updates?.Dispose());
            Guard("Shutdown", () => _taskbarElement?.Dispose());
            Guard("Shutdown", () => _deviceIcons?.Dispose());
            Guard("Shutdown", () => _trayIcon?.Dispose());
            Guard("Shutdown", () => _flyout?.Dispose());
        }
        finally
        {
            Exit();
        }
    }
}
