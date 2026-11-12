using System.Runtime.InteropServices;
using Jelling.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace Jelling.App;

/// <summary>
/// Borderless, always-on-top window that behaves like a Windows 11 tray flyout:
/// opens bottom-right on the monitor under the cursor and hides when it loses focus.
/// </summary>
public sealed partial class FlyoutWindow : Window, IDisposable
{
    // Wider than the quick settings' 360 so the name keeps room next to the threshold list; the widget
    // buttons add their own width, so they take none of the name's
    private const double WidthEpx = 420;

    // A tray click first deactivates (hides) the open flyout; without this the same click reopens it
    private const long ReopenGuardMs = 300;

    // Far outside every monitor, as Windows itself places minimized windows
    private const int OffScreen = -32000;

    // The row template's measures in FlyoutWindow.xaml, for the header above the threshold lists
    private const double ToggleWidth = 32;
    private const double ToggleSpacing = 4;
    private const double RowColumnSpacing = 12;
    private const double RowRightPadding = 12;

    private DisplayArea? _displayArea;
    private double _scale = 1.0;
    private bool _hasTaskbar = true;
    private long _hiddenAt;
    private bool _disposed;
    private double _widthEpx = WidthEpx;

    // A refresh came while a threshold list was open; its rows are rebuilt once the list closes
    private bool _rowsOutdated;

    public FlyoutWindow()
    {
        InitializeComponent();
        SetDevices([]);
        VersionText.Text = ReleaseVersion.Format(UpdateChecker.CurrentVersion);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        Activated += OnActivated;

        // Before the first show the content is not in the visual tree yet and measures as 0
        Root.Loaded += (_, _) => Place();

        // Alt+F4 would destroy the window; the tray app reuses it instead
        AppWindow.Closing += (_, args) =>
        {
            if (!_disposed)
            {
                args.Cancel = true;
                HideFlyout();
            }
        };
    }

    /// <summary>Raised with the device id and the state its "show in taskbar" button now shows.</summary>
    public event EventHandler<(Guid DeviceId, bool Shown)>? TaskbarToggled;

    /// <summary>Raised with the device id and the state its "show on the widget" button now shows.</summary>
    public event EventHandler<(Guid DeviceId, bool Shown)>? WidgetToggled;

    /// <summary>Raised with the device id and whether to connect it, the opposite of what its row shows.</summary>
    public event EventHandler<(Guid DeviceId, bool Connect)>? ConnectClicked;

    /// <summary>Raised when a device's low threshold is picked from its list.</summary>
    public event EventHandler<(Guid DeviceId, int Threshold)>? LowThresholdChanged;

    /// <summary>Raised when a threshold list closes after <see cref="SetDevices"/> was skipped for it.</summary>
    public event EventHandler? RowsOutdated;

    public void SetDevices(IReadOnlyList<DeviceItem> devices)
    {
        // Most refreshes change nothing; rebuilding the rows anyway would take the keyboard focus off them
        if (DeviceList.ItemsSource is IReadOnlyList<DeviceItem> shown && shown.SequenceEqual(devices))
        {
            return;
        }

        // Rebuilding the rows would close the open list under the user
        if (IsThresholdListOpen())
        {
            _rowsOutdated = true;
            return;
        }

        // A connect request is answered by a rebuild some time later, not by the click; the focus stays on its control.
        // The first call comes from the constructor, before the window has a XamlRoot to ask
        var focused = Root.XamlRoot is { } xamlRoot && FocusManager.GetFocusedElement(xamlRoot) is Control
            { DataContext: DeviceItem focusedItem, Name: { Length: > 0 } focusedName }
            ? (focusedItem.Id, focusedName)
            : default((Guid DeviceId, string Name)?);

        DeviceList.ItemsSource = devices;
        if (focused is { } target)
        {
            FocusInRow<Control>(target.DeviceId, target.Name);
        }

        // The header stays above the threshold lists however many buttons follow them
        var extraButtons = (devices.Any(d => d.ShowsWidgetToggle) ? ToggleWidth + ToggleSpacing : 0)
            + (devices.Any(d => d.ShowsConnectSlot) ? ToggleWidth + ToggleSpacing : 0);
        var buttons = ToggleWidth + extraButtons;
        _widthEpx = WidthEpx + extraButtons;
        LowThresholdHeader.Margin = new Thickness(0, 0, buttons + RowColumnSpacing + RowRightPadding, 2);
        DeviceList.Visibility = devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = devices.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        if (AppWindow.IsVisible)
        {
            Place();
        }
    }

    public void ShowUpdate(Version latest)
    {
        var text = string.Format(Strings.Get("UpdateAvailable"), ReleaseVersion.Format(latest));
        ToolTipService.SetToolTip(UpdateLink, text);
        AutomationProperties.SetName(UpdateLink, text);
        UpdateLink.Visibility = Visibility.Visible;

        if (AppWindow.IsVisible)
        {
            Place();
        }
    }

    public void Toggle()
    {
        if (AppWindow.IsVisible)
        {
            HideFlyout();
        }
        else if (Environment.TickCount64 - _hiddenAt > ReopenGuardMs)
        {
            ShowFlyout();
        }
    }

    public void Open()
    {
        if (AppWindow.IsVisible)
        {
            Activate();
        }
        else
        {
            ShowFlyout();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Close();
    }

    private void ShowFlyout()
    {
        GetCursorPos(out var cursor);
        _displayArea = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
        var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        _scale = GetScale(monitor);
        _hasTaskbar = HasTaskbar(monitor);

        // The first time the content cannot be measured before the window shows; it opens off screen
        // then, and Root.Loaded moves it into place, instead of flashing at the default position
        if (!Place())
        {
            AppWindow.Move(new PointInt32(OffScreen, OffScreen));
        }

        AppWindow.Show();
        Activate();
    }

    // False when the content does not measure yet
    private bool Place()
    {
        if (_displayArea is null)
        {
            return false;
        }

        // Measured in effective pixels, so a larger Windows text size grows the flyout with it
        Root.Measure(new Size(_widthEpx, double.PositiveInfinity));
        var heightEpx = Root.DesiredSize.Height;
        if (heightEpx <= 0)
        {
            return false;
        }

        var bounds = FlyoutPlacement.Calculate(
            ToPixelRect(_displayArea.WorkArea),
            ToPixelRect(_displayArea.OuterBounds),
            _hasTaskbar,
            (int)Math.Round(_widthEpx * _scale),
            (int)Math.Ceiling(heightEpx * _scale),
            _scale);

        // Moving onto a monitor with another DPI makes the system rescale the window once;
        // move first, then set the final size so that rescale cannot distort it
        AppWindow.Move(new PointInt32(bounds.X, bounds.Y));
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        return true;
    }

    private void HideFlyout()
    {
        AppWindow.Hide();
        _hiddenAt = Environment.TickCount64;
    }

    private void OnCloseClick(object sender, RoutedEventArgs args) => HideFlyout();

    // Events inside a DataTemplate reach the window; the clicked row is the button's DataContext
    private void OnTaskbarClick(object sender, RoutedEventArgs args) =>
        OnShownClick(sender, TaskbarToggled, item => !item.InTaskbar);

    private void OnWidgetClick(object sender, RoutedEventArgs args) =>
        OnShownClick(sender, WidgetToggled, item => !item.OnWidget);

    // The row's state, not the button's: a row not rebuilt after an unchanged list keeps its button's own
    // IsChecked, which can differ from the settings; the user asked for the opposite of what the row shows
    private void OnShownClick(
        object sender, EventHandler<(Guid DeviceId, bool Shown)>? toggled, Func<DeviceItem, bool> shown)
    {
        if (sender is FrameworkElement { DataContext: DeviceItem item } button)
        {
            var name = button.Name;
            toggled?.Invoke(this, (item.Id, shown(item)));
            FocusInRow<ToggleButton>(item.Id, name);
        }
    }

    // The button keeps showing the device's state until Windows reports the new one and the row is rebuilt;
    // a request the device refuses changes nothing, and nothing would set the button back
    private void OnConnectClick(object sender, RoutedEventArgs args)
    {
        if (sender is ToggleButton { DataContext: DeviceItem item } button)
        {
            button.IsChecked = item.IsConnected;
            ConnectClicked?.Invoke(this, (item.Id, !item.IsConnected));
        }
    }

    private void OnLowThresholdChanged(object sender, SelectionChangedEventArgs args)
    {
        // The binding's first selection when the row is created removes nothing; that is not a choice
        if (args.RemovedItems.Count == 0)
        {
            return;
        }

        if (sender is ComboBox { DataContext: DeviceItem item, SelectedIndex: >= 0 and var index })
        {
            // A recycled row selects its new device's value, removing the old one; that is no choice either
            if (index == item.LowThresholdIndex)
            {
                return;
            }

            LowThresholdChanged?.Invoke(this, (item.Id, BatteryLevel.LowThresholds[index]));

            // The row's low state follows the new threshold once rebuilt; queued so the list finishes
            // its selection first, and still open it defers the rebuild until it closes
            DispatcherQueue.TryEnqueue(() =>
            {
                RowsOutdated?.Invoke(this, EventArgs.Empty);
                if (!_rowsOutdated)
                {
                    FocusInRow<ComboBox>(item.Id);
                }
            });
        }
    }

    private void OnThresholdListClosed(object? sender, object args)
    {
        if (_rowsOutdated)
        {
            _rowsOutdated = false;

            // Not the skipped list itself: it predates the threshold just picked
            RowsOutdated?.Invoke(this, EventArgs.Empty);

            // The rebuild replaced the list that had the focus
            if (sender is FrameworkElement { DataContext: DeviceItem item })
            {
                FocusInRow<ComboBox>(item.Id);
            }
        }
    }

    private bool IsThresholdListOpen() =>
        Enumerable.Range(0, DeviceList.Items.Count)
            .Select(DeviceList.ContainerFromIndex)
            .OfType<ListViewItem>()
            .Any(container => container.ContentTemplateRoot is Panel row
                && row.Children.OfType<ComboBox>().Any(list => list.IsDropDownOpen));

    // A rebuild replaced the rows and with them the focused control; keyboard users stay in place.
    // The toggle buttons sit in panels of their own inside the row, and only their name tells them apart
    private void FocusInRow<T>(Guid deviceId, string? name = null) where T : Control
    {
        var index = DeviceList.Items.Cast<DeviceItem>().ToList().FindIndex(d => d.Id == deviceId);
        DeviceList.UpdateLayout();
        if (DeviceList.ContainerFromIndex(index) is ListViewItem { ContentTemplateRoot: Panel row }
            && Descendants(row)
                .OfType<T>()
                .FirstOrDefault(control => name is null || control.Name == name) is { } control)
        {
            control.Focus(FocusState.Programmatic);
        }
    }

    private static IEnumerable<UIElement> Descendants(Panel panel) =>
        panel.Children.SelectMany(child => child is Panel inner ? Descendants(inner) : [child]);

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            HideFlyout();
            args.Handled = true;
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (AppWindow.IsVisible)
            {
                HideFlyout();
            }

            return;
        }

        // Activation focuses a control and draws its keyboard focus rectangle;
        // programmatic focus once activation is done keeps Esc working without drawing it
        DeviceList.Focus(FocusState.Programmatic);
    }

    private static PixelRect ToPixelRect(RectInt32 rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static double GetScale(IntPtr monitor) =>
        GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;

    // "Show taskbar on all displays" turned off leaves the other monitors without one
    private static bool HasTaskbar(IntPtr monitor)
    {
        if (MonitorFromWindow(NativeMethods.FindWindow("Shell_TrayWnd", null), MonitorDefaultToNearest) == monitor)
        {
            return true;
        }

        for (var taskbar = NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_SecondaryTrayWnd", null);
             taskbar != IntPtr.Zero;
             taskbar = NativeMethods.FindWindowEx(IntPtr.Zero, taskbar, "Shell_SecondaryTrayWnd", null))
        {
            if (MonitorFromWindow(taskbar, MonitorDefaultToNearest) == monitor)
            {
                return true;
            }
        }

        return false;
    }

    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
