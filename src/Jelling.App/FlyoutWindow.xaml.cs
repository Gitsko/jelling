using System.Runtime.InteropServices;
using Jelling.Core;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
    // Wider than the quick settings' 360 so the name keeps room next to the threshold list
    private const int WidthEpx = 420;

    // A tray click first deactivates (hides) the open flyout; without this the same click reopens it
    private const long ReopenGuardMs = 300;

    private static readonly ResourceLoader Resources = new();

    private DisplayArea? _displayArea;
    private double _scale = 1.0;
    private bool _hasTaskbar = true;
    private long _hiddenAt;
    private bool _disposed;

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

    /// <summary>Raised with the device id when its "show in taskbar" button is clicked.</summary>
    public event EventHandler<Guid>? PinToggled;

    /// <summary>Raised when a device's low threshold is picked from its list.</summary>
    public event EventHandler<(Guid DeviceId, int Threshold)>? LowThresholdChanged;

    /// <summary>Raised when a threshold list closes after <see cref="SetDevices"/> was skipped for it.</summary>
    public event EventHandler? RowsOutdated;

    public void SetDevices(IReadOnlyList<DeviceItem> devices)
    {
        // Rebuilding the rows would close the open list under the user
        if (IsThresholdListOpen())
        {
            _rowsOutdated = true;
            return;
        }

        DeviceList.ItemsSource = devices;
        DeviceList.Visibility = devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = devices.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        if (AppWindow.IsVisible)
        {
            Place();
        }
    }

    public void ShowUpdate(Version latest)
    {
        var text = string.Format(Resources.GetString("UpdateAvailable"), ReleaseVersion.Format(latest));
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

        Place();
        AppWindow.Show();
        Activate();
    }

    private void Place()
    {
        if (_displayArea is null)
        {
            return;
        }

        // Measured in effective pixels, so a larger Windows text size grows the flyout with it
        Root.Measure(new Size(WidthEpx, double.PositiveInfinity));
        var heightEpx = Root.DesiredSize.Height;
        if (heightEpx <= 0)
        {
            return;
        }

        var bounds = FlyoutPlacement.Calculate(
            ToPixelRect(_displayArea.WorkArea),
            ToPixelRect(_displayArea.OuterBounds),
            _hasTaskbar,
            (int)Math.Round(WidthEpx * _scale),
            (int)Math.Ceiling(heightEpx * _scale),
            _scale);

        // Moving onto a monitor with another DPI makes the system rescale the window once;
        // move first, then set the final size so that rescale cannot distort it
        AppWindow.Move(new PointInt32(bounds.X, bounds.Y));
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    private void HideFlyout()
    {
        AppWindow.Hide();
        _hiddenAt = Environment.TickCount64;
    }

    private void OnCloseClick(object sender, RoutedEventArgs args) => HideFlyout();

    // Events inside a DataTemplate reach the window; the clicked row is the button's DataContext
    private void OnPinClick(object sender, RoutedEventArgs args)
    {
        if (((FrameworkElement)sender).DataContext is DeviceItem item)
        {
            PinToggled?.Invoke(this, item.Id);
            FocusInRow<Button>(item.Id);
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
        }
    }

    private bool IsThresholdListOpen() =>
        Enumerable.Range(0, DeviceList.Items.Count)
            .Select(DeviceList.ContainerFromIndex)
            .OfType<ListViewItem>()
            .Any(container => container.ContentTemplateRoot is Panel row
                && row.Children.OfType<ComboBox>().Any(list => list.IsDropDownOpen));

    // A rebuild replaced the rows and with them the focused control; keyboard users stay in place
    private void FocusInRow<T>(Guid deviceId) where T : Control
    {
        var index = DeviceList.Items.Cast<DeviceItem>().ToList().FindIndex(d => d.Id == deviceId);
        DeviceList.UpdateLayout();
        if (DeviceList.ContainerFromIndex(index) is ListViewItem { ContentTemplateRoot: Panel row }
            && row.Children.OfType<T>().FirstOrDefault() is { } control)
        {
            control.Focus(FocusState.Programmatic);
        }
    }

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
        if (MonitorFromWindow(FindWindow("Shell_TrayWnd", null), MonitorDefaultToNearest) == monitor)
        {
            return true;
        }

        for (var taskbar = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_SecondaryTrayWnd", null);
             taskbar != IntPtr.Zero;
             taskbar = FindWindowEx(IntPtr.Zero, taskbar, "Shell_SecondaryTrayWnd", null))
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
