using Jelling.Core;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.AppLifecycle;
using Windows.UI.ViewManagement;

namespace Jelling.App;

public partial class App : Application
{
    private const string TrayGlyph = "\uE702";

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
    private System.Drawing.Color _trayColor;
    private DeviceColors _deviceColors = null!;

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
        _trayColor = TrayColor();
        _deviceColors = CurrentDeviceColors();
        _ = RefreshAsync();

        _watcher = new DeviceChangeWatcher();
        _watcher.Changed += (_, _) => dispatcher.TryEnqueue(() => _ = RefreshAsync());
        _watcher.Start();

        // A second start was redirected here by Program.Main; open the flyout instead of a second tray icon
        AppInstance.GetCurrent().Activated += (_, _) => dispatcher.TryEnqueue(OpenFlyout);

        var resources = new ResourceLoader();
        var title = resources.GetString("FlyoutHeader/Text");

        // PopupMenu mode turns the flyout into a native menu that only runs Command, not Click
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = title, IsEnabled = false });
        menu.Items.Add(new MenuFlyoutSeparator());
        var autoStart = new ToggleMenuFlyoutItem { Text = resources.GetString("AutoStart"), IsChecked = AutoStart.IsEnabled };
        // The native menu does not flip IsChecked itself; read the registry back so the tick shows what is stored
        autoStart.Command = new RelayCommand(() =>
        {
            AutoStart.Set(!AutoStart.IsEnabled);
            autoStart.IsChecked = AutoStart.IsEnabled;
        });
        menu.Items.Add(autoStart);

        var showPercent = new ToggleMenuFlyoutItem { Text = resources.GetString("ShowPercent"), IsChecked = _settings.ShowPercent };
        showPercent.Command = new RelayCommand(() =>
        {
            SetShowPercent(!_settings.ShowPercent);
            showPercent.IsChecked = _settings.ShowPercent;
        });
        menu.Items.Add(showPercent);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = resources.GetString("Exit"), Command = new RelayCommand(Shutdown) });

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = title,
            Icon = TrayIconRenderer.Render(TrayGlyph, _trayColor),
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(ToggleFlyout),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
        };

        // false: keep the process out of Efficiency Mode so the flyout opens without delay
        _trayIcon.ForceCreate(false);

        // Raised for theme, accent and high-contrast changes, often several times per change
        _uiSettings.ColorValuesChanged += (_, _) => dispatcher.TryEnqueue(ApplyTheme);

        // A toast clicked after the app had ended starts it again; NotificationInvoked only covers a running app
        if (AppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.AppNotification)
        {
            OpenFlyout();
        }
    }

    private void ApplyTheme()
    {
        try
        {
            if (_shuttingDown || _trayIcon is null)
            {
                return;
            }

            var color = TrayColor();
            var deviceColors = CurrentDeviceColors();
            if (color == _trayColor && deviceColors == _deviceColors)
            {
                return;
            }

            _trayColor = color;
            _deviceColors = deviceColors;

            // TaskbarIcon does not own the image it shows
            var previous = _trayIcon.Icon;
            _trayIcon.Icon = TrayIconRenderer.Render(TrayGlyph, color);
            previous?.Dispose();

            // Device icons re-render because their look includes the color; the flyout rows follow
            // the theme by themselves, and rebuilding them would close an open threshold list
            ShowInTaskbar();
        }
        catch (Exception exception)
        {
            // The icons keep their old color; a failed redraw must not end the tray app
            CrashLog.Write("Theme", exception);
        }
    }

    // Dark blue loses its contrast on a dark taskbar, so that one gets a light blue;
    // high contrast themes set their own taskbar colors and ignore the light/dark setting
    private System.Drawing.Color TrayColor()
    {
        if (new AccessibilitySettings().HighContrast)
        {
            return HighContrastTextColor();
        }

        return TaskbarIsLight()
            ? System.Drawing.Color.FromArgb(0x00, 0x3A, 0x8C)
            : System.Drawing.Color.FromArgb(0x6C, 0xB4, 0xFF);
    }

    // Device icons look like the system's own tray icons: black or white, no brand color. Low levels take
    // the Windows critical colors: dark red on a light taskbar, light red on a dark one, where dark red
    // would be too faint for small digits. High contrast themes allow only their own colors
    private DeviceColors CurrentDeviceColors()
    {
        if (new AccessibilitySettings().HighContrast)
        {
            var text = HighContrastTextColor();
            return new DeviceColors(text, text);
        }

        return TaskbarIsLight()
            ? new DeviceColors(System.Drawing.Color.Black, System.Drawing.Color.FromArgb(0xC4, 0x2B, 0x1C))
            : new DeviceColors(System.Drawing.Color.White, System.Drawing.Color.FromArgb(0xFF, 0x99, 0xA4));
    }

    private System.Drawing.Color HighContrastTextColor()
    {
        var text = _uiSettings.UIElementColor(UIElementType.WindowText);
        return System.Drawing.Color.FromArgb(text.R, text.G, text.B);
    }

    // The taskbar follows the system theme, not the app theme; line glyphs need its contrast color
    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

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

        var badges = DeviceBadge.ForPinned(_devices, _settings, _deviceColors);
        var inElement = _taskbarElement?.Show(badges) == true;
        _deviceIcons?.Update(inElement ? [] : badges);
    }

    private void ShowInTaskbarAfterRecheck()
    {
        try
        {
            ShowInTaskbar();
        }
        catch (Exception exception)
        {
            // Raised from the element's timer; a failed redraw must not end the tray app
            CrashLog.Write("Redraw", exception);
        }
    }

    private void WarnLow()
    {
        foreach (var device in _lowBattery.Check(_devices, _settings.LowThreshold))
        {
            try
            {
                _toasts?.ShowLow(device);
            }
            catch (Exception exception)
            {
                // One failed toast must not cost the other devices theirs; the tray and flyout still show it
                CrashLog.Write("Toast", exception);
            }
        }
    }

    private void TogglePin(Guid deviceId)
    {
        try
        {
            _settings.TogglePin(deviceId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The pin still applies until the app ends; only saving it failed
            CrashLog.Write("Settings", exception);
        }

        // From the last list: the pin is a local change, there is nothing new to ask Windows for
        ShowLastDevices();
    }

    private void ShowLastDevices()
    {
        try
        {
            ShowDevices();
        }
        catch (Exception exception)
        {
            // Raised from the flyout; a failed redraw must not end the tray app
            CrashLog.Write("Redraw", exception);
        }
    }

    private void SetShowPercent(bool show)
    {
        try
        {
            _settings.SetShowPercent(show);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The choice still applies until the app ends; only saving it failed
            CrashLog.Write("Settings", exception);
        }

        try
        {
            ShowInTaskbar();
        }
        catch (Exception exception)
        {
            // Raised from the menu; a failed redraw must not end the tray app
            CrashLog.Write("Redraw", exception);
        }
    }

    private void SetLowThreshold(Guid deviceId, int threshold)
    {
        // A recycled row selects its new device's value; that is no choice and must not pin the default
        if (_settings.LowThreshold(deviceId) == threshold)
        {
            return;
        }

        try
        {
            _settings.SetLowThreshold(deviceId, threshold);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The threshold still applies until the app ends; only saving it failed
            CrashLog.Write("Settings", exception);
        }

        try
        {
            // The flyout already shows the choice; rebuilding its rows would close the list under the user
            ShowInTaskbar();

            // A warning at the old threshold must not swallow the first one at the new threshold;
            // a threshold raised above the current level warns at once
            _lowBattery.Reset(deviceId);
            WarnLow();
        }
        catch (Exception exception)
        {
            // Raised from the flyout; a failed redraw must not end the tray app
            CrashLog.Write("LowThreshold", exception);
        }
    }

    private void Shutdown()
    {
        _shuttingDown = true;

        // A start during shutdown would otherwise be redirected to this dying instance and get lost
        AppInstance.GetCurrent().UnregisterKey();
        _watcher?.Dispose();
        _toasts?.Dispose();
        _updates?.Dispose();
        _taskbarElement?.Dispose();
        _deviceIcons?.Dispose();
        _trayIcon?.Dispose();
        _flyout?.Dispose();
        Exit();
    }
}
