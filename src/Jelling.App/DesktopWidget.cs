using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using Jelling.Core;
using Microsoft.UI.Dispatching;

namespace Jelling.App;

/// <summary>
/// The pinned devices on the desktop, drawn like the taskbar element on a plate of their own, since any wallpaper
/// may lie behind them: a layered child window of the window
/// that holds Explorer's desktop icons, so it stays behind all other windows and still shows on Win+D.
/// Unlocked, it moves with a drag; a click opens the flyout either way.
/// </summary>
/// <remarks>
/// As a child window of Explorer's it shares Explorer's input queue, like <see cref="TaskbarElement"/>:
/// nothing on this thread may block, the drag included.
/// </remarks>
public sealed partial class DesktopWidget : IDisposable
{
    private const string ClassName = "JellingDesktopWidget";

    // As tall as the taskbar at its normal size, so the devices look the same in both places
    private const double HeightEpx = 48;
    private const double MarginEpx = 24;

    private readonly Action _onClick;
    private readonly Action _onRightClick;
    private readonly Func<bool> _isLocked;
    private readonly Action<PixelPoint> _onMoved;
    private readonly DispatcherQueueTimer _timer;

    // Held in a field: Windows calls it for as long as the class is registered
    private readonly WndProc _wndProc;

    private IReadOnlyList<DeviceBadge> _badges = [];
    private DeviceColors? _colors;
    private WidgetPlate? _plate;
    private PixelPoint? _stored;
    private int _sizeFactor = 1;
    private Bitmap? _strip;
    private IReadOnlyList<Rectangle> _badgeBounds = [];
    private uint _stripDpi;
    private IntPtr _window;
    private IntPtr _toolTip;
    private int _toolCount;
    private IntPtr _host;
    private PixelPoint? _drawnAt;
    private string? _lastError;

    // Where the left button went down, in screen pixels, and where the widget was then
    private bool _pressed;
    private bool _dragging;
    private Point32 _pressCursor;
    private PixelPoint _pressPosition;

    /// <param name="onRightClick">Opens the app's menu at the cursor.</param>
    public DesktopWidget(Action onClick, Action onRightClick, Func<bool> isLocked, Action<PixelPoint> onMoved)
    {
        _onClick = onClick;
        _onRightClick = onRightClick;
        _isLocked = isLocked;
        _onMoved = onMoved;
        _wndProc = OnMessage;

        var windowClass = new WndClassEx
        {
            Size = (uint)Marshal.SizeOf<WndClassEx>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            Instance = GetModuleHandle(null),
            ClassName = ClassName,
        };
        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw new Win32Exception();
        }

        var controls = new InitCommonControlsExInfo { Size = (uint)Marshal.SizeOf<InitCommonControlsExInfo>(), Classes = IccBarClasses };
        InitCommonControlsEx(ref controls);

        // Explorer recreates the desktop after a restart, and monitors or their scale can change; following
        // that takes only window and monitor lookups
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => TryPlace();
    }

    /// <summary>Shows the badges on the desktop; an empty list removes the widget.</summary>
    /// <param name="stored">Where the user left the widget; null until it was first moved.</param>
    /// <param name="sizeFactor">One of <see cref="DeviceSettings.WidgetScales"/>; it keeps its top left corner as it grows.</param>
    /// <param name="plate">The surface the devices lie on.</param>
    public void Show(IReadOnlyList<DeviceBadge> badges, DeviceColors colors, WidgetPlate plate, PixelPoint? stored, int sizeFactor)
    {
        if (!badges.SequenceEqual(_badges) || colors != _colors || plate != _plate || sizeFactor != _sizeFactor)
        {
            _badges = badges;
            _colors = colors;
            _plate = plate;
            _sizeFactor = sizeFactor;
            ReleaseStrip();
        }

        _stored = stored;
        if (_badges.Count == 0)
        {
            _timer.Stop();
            DestroyWidget();
            return;
        }

        _timer.Start();

        // A refresh during a drag would pull the widget back to where it was; the drop places it
        if (!_dragging)
        {
            TryPlace();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        DestroyWidget();
        ReleaseStrip();
        UnregisterClass(ClassName, GetModuleHandle(null));
    }

    private void TryPlace()
    {
        if (_dragging)
        {
            return;
        }

        try
        {
            Place();
            _lastError = null;
        }
        catch (Exception exception)
        {
            // E.g. while Explorer restarts; the next check tries again. A lasting failure would otherwise be
            // logged every second
            var error = $"{exception.GetType()}: {exception.Message}";
            if (error != _lastError)
            {
                CrashLog.Write("DesktopWidget", exception);
                _lastError = error;
            }

            DestroyWidget();
        }
    }

    private void Place()
    {
        if (_badges.Count == 0)
        {
            DestroyWidget();
            return;
        }

        var host = FindDesktopHost();
        if (host == IntPtr.Zero)
        {
            DestroyWidget();
            return;
        }

        var (workAreas, primary) = WorkAreas();

        // Rendered for the monitor it is going to, which may have another scale than the primary one
        var anchor = _stored ?? new PixelPoint(primary.Right - 1, primary.Y);
        var dpi = DpiAt(anchor);
        if (_strip is null || _stripDpi != dpi)
        {
            ReleaseStrip();
            var scale = dpi / 96.0 * _sizeFactor;
            (_strip, _badgeBounds) = TaskbarStripRenderer.Render(_badges, _colors!, (int)Math.Round(HeightEpx * scale), scale, _plate);
            _stripDpi = dpi;
        }

        var margin = (int)Math.Round(MarginEpx * dpi / 96.0);
        var position = WidgetPlacement.Place(_stored, _strip.Width, _strip.Height, margin, primary, workAreas);

        if (_host != host || !IsWindow(_window))
        {
            DestroyWidget();
            Create(host);
        }

        // The desktop icons are a sibling window covering the whole desktop; the widget must stay above them
        if (GetWindow(host, GwChild) != _window)
        {
            SetWindowPos(_window, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        if (_drawnAt != position)
        {
            Draw(position);
        }
    }

    // The view of the desktop icons. Its parent is Progman since Windows 11 24H2, before that a WorkerW
    // Explorer splits off when it animates the wallpaper
    private static IntPtr FindDesktopHost()
    {
        var progman = NativeMethods.FindWindow("Progman", null);
        var view = progman == IntPtr.Zero ? IntPtr.Zero : NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        var worker = IntPtr.Zero;
        while (view == IntPtr.Zero && (worker = NativeMethods.FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        {
            view = NativeMethods.FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
        }

        return view;
    }

    private static (IReadOnlyList<PixelRect> All, PixelRect Primary) WorkAreas()
    {
        var all = new List<PixelRect>();
        PixelRect? primary = null;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var work = info.Work;
                var area = new PixelRect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
                all.Add(area);
                if ((info.Flags & MonitorInfoFPrimary) != 0)
                {
                    primary = area;
                }
            }

            return true;
        }, IntPtr.Zero);

        if (all.Count == 0)
        {
            throw new InvalidOperationException("No monitor found");
        }

        return (all, primary ?? all[0]);
    }

    private static uint DpiAt(PixelPoint point)
    {
        var monitor = MonitorFromPoint(new Point32 { X = point.X, Y = point.Y }, MonitorDefaultToNearest);
        return GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX != 0 ? dpiX : 96;
    }

    private void Create(IntPtr host)
    {
        _window = CreateWindowEx(
            WsExLayered | WsExNoParentNotify, ClassName, "", WsChild | WsVisible,
            0, 0, 0, 0, host, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (_window == IntPtr.Zero)
        {
            throw new Win32Exception();
        }

        _host = host;
        _drawnAt = null;

        // Without an owner: a child window as owner would make Explorer's desktop the owner instead
        _toolTip = CreateWindowEx(
            WsExTopmost, "tooltips_class32", null, WsPopup | TtsAlwaysTip | TtsNoPrefix,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
    }

    // Per-pixel alpha keeps the wallpaper visible around the glyphs and digits
    private void Draw(PixelPoint position)
    {
        var screen = IntPtr.Zero;
        var memory = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            screen = GetDC(IntPtr.Zero);
            memory = CreateCompatibleDC(screen);
            bitmap = _strip!.GetHbitmap(Color.FromArgb(0));
            previous = SelectObject(memory, bitmap);

            // For a child window the position is in the parent's client coordinates
            var client = ToHostClient(position);
            var size = new Size32 { Width = _strip.Width, Height = _strip.Height };
            var source = new Point32();
            var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = AcSrcAlpha };
            if (!UpdateLayeredWindow(_window, screen, ref client, ref size, memory, ref source, 0, ref blend, UlwAlpha))
            {
                throw new Win32Exception();
            }

            _drawnAt = position;
        }
        finally
        {
            if (previous != IntPtr.Zero)
            {
                SelectObject(memory, previous);
            }

            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (memory != IntPtr.Zero)
            {
                DeleteDC(memory);
            }

            if (screen != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        Describe();
    }

    // The desktop spans all monitors; with one left of or above the primary its origin is negative
    private Point32 ToHostClient(PixelPoint position)
    {
        var point = new Point32 { X = position.X, Y = position.Y };
        if (!ScreenToClient(_host, ref point))
        {
            throw new Win32Exception();
        }

        return point;
    }

    // One tooltip per device, and the same text as the window's name for screen readers
    private void Describe()
    {
        SetWindowText(_window, string.Join(", ", _badges.Select(badge => badge.ToolTip)));
        if (_toolTip == IntPtr.Zero)
        {
            return;
        }

        for (var id = 0; id < _toolCount; id++)
        {
            var stale = NewToolInfo(id);
            SendMessage(_toolTip, TtmDelTool, IntPtr.Zero, ref stale);
        }

        for (var id = 0; id < _badges.Count; id++)
        {
            var tool = NewToolInfo(id);
            tool.Flags = TtfSubclass;
            tool.Bounds = Rect32.From(_badgeBounds[id]);
            tool.Text = _badges[id].ToolTip;
            SendMessage(_toolTip, TtmAddTool, IntPtr.Zero, ref tool);
        }

        _toolCount = _badges.Count;
    }

    private ToolInfo NewToolInfo(int id) =>
        new() { Size = (uint)Marshal.SizeOf<ToolInfo>(), Window = _window, Id = id };

    private void DestroyWidget()
    {
        _pressed = false;
        _dragging = false;
        if (_toolTip != IntPtr.Zero)
        {
            DestroyWindow(_toolTip);
            _toolTip = IntPtr.Zero;
            _toolCount = 0;
        }

        if (_window != IntPtr.Zero)
        {
            // Already gone when Explorer restarted; destroying it again only fails
            DestroyWindow(_window);
            _window = IntPtr.Zero;
        }

        _host = IntPtr.Zero;
        _drawnAt = null;
    }

    private void ReleaseStrip()
    {
        _strip?.Dispose();
        _strip = null;
        _badgeBounds = [];
        _drawnAt = null;
    }

    private IntPtr OnMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            // Activating the desktop would first close an open flyout, and the click would then reopen it
            case WmMouseActivate:
                return MaNoActivate;

            // DefWindowProc would ask the parent first, Explorer's desktop, across processes
            case WmSetCursor:
                SetCursor(LoadCursor(IntPtr.Zero, _isLocked() ? IdcArrow : IdcSizeAll));
                return 1;

            case WmLButtonDown:
                SetCapture(window);
                GetCursorPos(out _pressCursor);
                _pressPosition = _drawnAt ?? default;
                _pressed = true;
                return IntPtr.Zero;
            case WmMouseMove:
                if (_pressed)
                {
                    Guard(Drag);
                }

                return IntPtr.Zero;

            // Lost without a button up, e.g. to another window taking the capture: the drag is dropped
            case WmCaptureChanged:
                if (_dragging)
                {
                    _dragging = false;
                    _drawnAt = null;
                    TryPlace();
                }

                _pressed = false;
                return IntPtr.Zero;
            case WmLButtonUp:
                var pressed = _pressed;
                var dragged = _dragging;
                _pressed = false;
                _dragging = false;
                ReleaseCapture();
                if (dragged)
                {
                    Drop();
                }
                else if (pressed)
                {
                    Click();
                }

                return IntPtr.Zero;

            // DefWindowProc would turn a right click into WM_CONTEXTMENU and send it to the parent, where
            // Explorer would open the desktop's own menu; the widget opens the app's menu instead
            case WmRButtonUp:
                Guard(_onRightClick);
                return IntPtr.Zero;
            case WmRButtonDown:
            case WmContextMenu:
                return IntPtr.Zero;

            // Explorer destroys the widget along with the desktop; the handle may then be reused
            case WmNcDestroy:
                if (window == _window)
                {
                    _window = IntPtr.Zero;
                }

                return DefWindowProc(window, message, wParam, lParam);
            default:
                return DefWindowProc(window, message, wParam, lParam);
        }
    }

    // Moves only once the cursor left the system's drag threshold, so a shaky click still opens the flyout.
    // Locked, a drag past the threshold moves nothing and is no click either
    private void Drag()
    {
        if (_drawnAt is null || !GetCursorPos(out var cursor))
        {
            return;
        }

        var dx = cursor.X - _pressCursor.X;
        var dy = cursor.Y - _pressCursor.Y;
        if (!_dragging && Math.Abs(dx) <= GetSystemMetrics(SmCxDrag) && Math.Abs(dy) <= GetSystemMetrics(SmCyDrag))
        {
            return;
        }

        if (_isLocked())
        {
            _pressed = false;
            return;
        }

        _dragging = true;
        var client = ToHostClient(new PixelPoint(_pressPosition.X + dx, _pressPosition.Y + dy));
        SetWindowPos(_window, IntPtr.Zero, client.X, client.Y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    // Stored as dropped; Place keeps it inside the monitor and renders it for that monitor's scale
    private void Drop()
    {
        if (!GetCursorPos(out var cursor))
        {
            _drawnAt = null;
            TryPlace();
            return;
        }

        _stored = new PixelPoint(_pressPosition.X + cursor.X - _pressCursor.X, _pressPosition.Y + cursor.Y - _pressCursor.Y);
        _drawnAt = null;
        TryPlace();

        // What Place made of it, so a drop half off the edge is stored where it is shown
        if (_drawnAt is { } placed)
        {
            _stored = placed;
            Guard(() => _onMoved(placed));
        }
    }

    private void Click() => Guard(_onClick);

    // An exception must not unwind into the native message loop
    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            CrashLog.Write("DesktopWidget", exception);
        }
    }
}
