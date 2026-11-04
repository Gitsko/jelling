using System.Runtime.InteropServices;

namespace Jelling.App;

/// <summary>
/// The Win32 calls more than one class needs. Calls only one class makes stay declared in that class.
/// </summary>
/// <remarks>
/// Flat functions are declared by hand. COM interfaces such as UI Automation's come from CsWin32 (listed in
/// NativeMethods.txt): written by hand, their vtable order would have to match the SDK headers exactly.
/// </remarks>
internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindow(string className, string? windowName);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(IntPtr window);

    [LibraryImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPosition(out CursorPoint point);

    /// <summary>The cursor in screen pixels, or null when Windows cannot tell, e.g. on the secure desktop.</summary>
    public static System.Drawing.Point? CursorPosition() =>
        GetCursorPosition(out var point) ? new System.Drawing.Point(point.X, point.Y) : null;

    /// <summary>The primary taskbar's DPI, which follows a scale change without signing out; 0 while it is missing.</summary>
    public static uint TaskbarDpi()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        return taskbar == IntPtr.Zero ? 0 : GetDpiForWindow(taskbar);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }
}
