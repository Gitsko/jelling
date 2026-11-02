using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using Jelling.Core;
using Microsoft.UI.Dispatching;

namespace Jelling.App;

/// <summary>
/// The pinned devices inside the taskbar itself, left of the notification area: a layered child window of
/// Explorer's taskbar. Windows 11 has no API for this; when the taskbar has no room or cannot be found,
/// <see cref="Show"/> reports false and the app falls back to tray icons.
/// </summary>
/// <remarks>
/// A child window of another process shares its input queue: while this UI thread is busy, the taskbar
/// does not respond either. Nothing on this thread may block, which is also why Explorer is never sent
/// <c>WM_PARENTNOTIFY</c>.
/// </remarks>
public sealed class TaskbarElement : IDisposable
{
    private const string ClassName = "JellingTaskbarElement";

    private readonly Action _onClick;
    private readonly DispatcherQueueTimer _timer;

    // Held in a field: Windows calls it for as long as the class is registered
    private readonly WndProc _wndProc;

    private IReadOnlyList<DeviceBadge> _badges = [];
    private Bitmap? _strip;
    private IReadOnlyList<Rectangle> _badgeBounds = [];
    private (uint Dpi, int Height) _stripRenderedFor;
    private IntPtr _window;
    private IntPtr _toolTip;
    private int _toolCount;
    private IntPtr _taskbar;
    private PixelRect? _drawnAt;
    private bool _pressed;
    private string? _lastError;
    private uint _lastTaskbarDpi;

    // True when no tray icons are needed: the element shows every badge, or there is none to show
    private bool _coversBadges;

    /// <summary>
    /// Raised when the element appears or gives way, so the tray icons can take over or step back, and when the
    /// taskbar's scale changes while they stand in, so they are drawn again at the new size.
    /// </summary>
    public event EventHandler? AvailabilityChanged;

    public TaskbarElement(Action onClick)
    {
        _onClick = onClick;
        _wndProc = OnMessage;

        var windowClass = new WndClassEx
        {
            Size = (uint)Marshal.SizeOf<WndClassEx>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            Instance = GetModuleHandle(null),
            Cursor = LoadCursor(IntPtr.Zero, IdcArrow),
            ClassName = ClassName,
        };
        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw new Win32Exception();
        }

        var controls = new InitCommonControlsExInfo { Size = (uint)Marshal.SizeOf<InitCommonControlsExInfo>(), Classes = IccBarClasses };
        InitCommonControlsEx(ref controls);

        // Explorer moves the notification area when icons come and go, and recreates the taskbar after a restart
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Recheck();
    }

    /// <summary>Shows the badges in the taskbar, replacing what it showed before; an empty list removes the element.</summary>
    /// <returns>False when the badges could not be shown in the taskbar and need tray icons instead.</returns>
    public bool Show(IReadOnlyList<DeviceBadge> badges)
    {
        if (!badges.SequenceEqual(_badges))
        {
            _badges = badges;
            ReleaseStrip();
        }

        if (_badges.Count == 0)
        {
            _timer.Stop();
        }
        else
        {
            _timer.Start();
        }

        _coversBadges = TryPlace();
        return _coversBadges;
    }

    public void Dispose()
    {
        _timer.Stop();
        DestroyElement();
        ReleaseStrip();
        UnregisterClass(ClassName, GetModuleHandle(null));
    }

    private void Recheck()
    {
        var covered = _coversBadges;
        _coversBadges = TryPlace();

        // The tray icons are rendered at the taskbar's icon size; scaled by Explorer their digits blur
        var taskbar = FindWindow("Shell_TrayWnd", null);
        var dpi = taskbar == IntPtr.Zero ? 0 : GetDpiForWindow(taskbar);
        var rescaled = dpi != 0 && _lastTaskbarDpi != 0 && dpi != _lastTaskbarDpi;
        if (dpi != 0)
        {
            _lastTaskbarDpi = dpi;
        }

        if (_coversBadges != covered || (rescaled && !_coversBadges))
        {
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool TryPlace()
    {
        try
        {
            var placed = Place();
            _lastError = null;
            if (!placed)
            {
                DestroyElement();
            }

            return placed;
        }
        catch (Exception exception)
        {
            // E.g. while Explorer restarts; the tray icons take over and the next check tries again.
            // A lasting failure would otherwise be logged every second
            var error = $"{exception.GetType()}: {exception.Message}";
            if (error != _lastError)
            {
                CrashLog.Write("TaskbarElement", exception);
                _lastError = error;
            }

            DestroyElement();
            return false;
        }
    }

    // False when the element cannot be placed; the caller then removes it
    private bool Place()
    {
        if (_badges.Count == 0)
        {
            DestroyElement();
            return true;
        }

        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
        {
            return false;
        }

        var notificationArea = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        var taskList = FindWindowEx(taskbar, IntPtr.Zero, "ReBarWindow32", null);
        if (notificationArea == IntPtr.Zero || taskList == IntPtr.Zero)
        {
            return false;
        }

        // The taskbar's own DPI, not the system's: it can sit on a monitor with a different scale
        var dpi = GetDpiForWindow(taskbar);
        var notificationBounds = BoundsOf(notificationArea);
        if (_strip is null || _stripRenderedFor != (dpi, notificationBounds.Height))
        {
            ReleaseStrip();
            (_strip, _badgeBounds) = TaskbarStripRenderer.Render(_badges, notificationBounds.Height, dpi / 96.0);
            _stripRenderedFor = (dpi, notificationBounds.Height);
        }

        var bounds = TaskbarPlacement.Calculate(BoundsOf(taskbar), notificationBounds, BoundsOf(taskList), _strip.Width);
        if (bounds is not { } placed)
        {
            return false;
        }

        if (_taskbar != taskbar || !IsWindow(_window))
        {
            DestroyElement();
            Create(taskbar);
        }

        // The taskbar's XAML content is a sibling window; the element must stay above it to be seen
        if (GetWindow(taskbar, GwChild) != _window)
        {
            SetWindowPos(_window, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        if (_drawnAt != placed)
        {
            Draw(placed);
        }

        return true;
    }

    private void Create(IntPtr taskbar)
    {
        _window = CreateWindowEx(
            WsExLayered | WsExNoParentNotify, ClassName, "", WsChild | WsVisible,
            0, 0, 0, 0, taskbar, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (_window == IntPtr.Zero)
        {
            throw new Win32Exception();
        }

        _taskbar = taskbar;
        _drawnAt = null;

        // Without an owner: a child window as owner would make Explorer's taskbar the owner instead
        _toolTip = CreateWindowEx(
            WsExTopmost, "tooltips_class32", null, WsPopup | TtsAlwaysTip | TtsNoPrefix,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
    }

    // Per-pixel alpha keeps the taskbar's acrylic visible around the glyphs and digits
    private void Draw(PixelRect bounds)
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

            // For a child window the position is in the parent's client coordinates. The bounds are relative
            // to the taskbar's window rectangle, which matches its client area as the taskbar has no border
            var position = new Point32 { X = bounds.X, Y = bounds.Y };
            var size = new Size32 { Width = _strip.Width, Height = _strip.Height };
            var source = new Point32();
            var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = AcSrcAlpha };
            if (!UpdateLayeredWindow(_window, screen, ref position, ref size, memory, ref source, 0, ref blend, UlwAlpha))
            {
                throw new Win32Exception();
            }

            _drawnAt = bounds;
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

    private void DestroyElement()
    {
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

        _taskbar = IntPtr.Zero;
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
            // Activating the taskbar would first close an open flyout, and the click would then reopen it
            case WmMouseActivate:
                return MaNoActivate;

            // A click counts only when it also started here, not when a drag across the taskbar ends here
            case WmLButtonDown:
                SetCapture(window);
                _pressed = true;
                return IntPtr.Zero;
            case WmCaptureChanged:
                _pressed = false;
                return IntPtr.Zero;
            case WmLButtonUp:
                var clicked = _pressed && IsOnStrip(lParam);
                ReleaseCapture();
                if (clicked)
                {
                    Click();
                }

                return IntPtr.Zero;

            // DefWindowProc would turn a right click into WM_CONTEXTMENU and send it to the parent, Explorer's
            // taskbar, across processes. Like the device tray icons, the element has no menu of its own
            case WmRButtonDown:
            case WmRButtonUp:
            case WmContextMenu:
                return IntPtr.Zero;

            // Explorer destroys the element along with the taskbar; the handle may then be reused
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

    private bool IsOnStrip(IntPtr lParam)
    {
        var packed = lParam.ToInt64();
        var x = (short)(packed & 0xFFFF);
        var y = (short)((packed >> 16) & 0xFFFF);
        return _strip is not null && x >= 0 && y >= 0 && x < _strip.Width && y < _strip.Height;
    }

    private void Click()
    {
        try
        {
            _onClick();
        }
        catch (Exception exception)
        {
            // An exception must not unwind into the native message loop
            CrashLog.Write("TaskbarElement", exception);
        }
    }

    private static PixelRect BoundsOf(IntPtr window)
    {
        if (!GetWindowRect(window, out var rect))
        {
            throw new Win32Exception();
        }

        return new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private const uint WsChild = 0x40000000;
    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsExNoParentNotify = 0x00000004;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExLayered = 0x00080000;
    private const uint TtsAlwaysTip = 0x01;
    private const uint TtsNoPrefix = 0x02;
    private const uint TtfSubclass = 0x0010;
    private const uint TtmAddTool = 0x0432;
    private const uint TtmDelTool = 0x0433;
    private const uint IccBarClasses = 0x00000004;
    private const uint WmNcDestroy = 0x0082;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmCaptureChanged = 0x0215;
    private const uint WmRButtonDown = 0x0204;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const int MaNoActivate = 3;
    private const uint GwChild = 5;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint UlwAlpha = 0x02;
    private const byte AcSrcAlpha = 0x01;
    private const int IdcArrow = 32512;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InitCommonControlsExInfo
    {
        public uint Size;
        public uint Classes;
    }

    // TTTOOLINFOW without lpReserved, a size every comctl32 version accepts
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ToolInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr Window;
        public IntPtr Id;
        public Rect32 Bounds;
        public IntPtr Instance;
        public string? Text;
        public IntPtr Param;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public static Rect32 From(Rectangle rectangle) =>
            new() { Left = rectangle.Left, Top = rectangle.Top, Right = rectangle.Right, Bottom = rectangle.Bottom };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size32
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr window, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr window, string text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, ref ToolInfo lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr window, IntPtr destination, ref Point32 position, ref Size32 size,
        IntPtr source, ref Point32 sourcePosition, uint key, ref BlendFunction blend, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, int cursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("comctl32.dll")]
    private static extern bool InitCommonControlsEx(ref InitCommonControlsExInfo info);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
