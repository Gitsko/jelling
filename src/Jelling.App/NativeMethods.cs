using System.Runtime.InteropServices;

namespace Jelling.App;

/// <summary>
/// The Win32 calls more than one class needs. Calls only one class makes stay declared in that class.
/// </summary>
internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindow(string className, string? windowName);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(IntPtr window);

    /// <summary>The primary taskbar's DPI, which follows a scale change without signing out; 0 while it is missing.</summary>
    public static uint TaskbarDpi()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        return taskbar == IntPtr.Zero ? 0 : GetDpiForWindow(taskbar);
    }
}
