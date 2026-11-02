using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace Jelling.App;

/// <summary>
/// Where the taskbar's app buttons end, read through UI Automation. On Windows 11 they are XAML content of the
/// taskbar; the task list window Win32 still exposes keeps its width while they grow up to the tray.
/// </summary>
/// <remarks>
/// Every call goes into Explorer and waits for it, so it must run on a thread-pool thread, one call at a time.
/// The automation object is created there too: created on the UI thread, it would live in that thread's
/// apartment, and each call from the pool would be marshaled back to the thread it must not block.
/// </remarks>
internal sealed class TaskButtons
{
    private const string ButtonClass = "Taskbar.TaskListButtonAutomationPeer";

    // UIA waits 20 s for a hung Explorer by default; the tray icons should not wait that long to take over
    private const uint TimeoutMilliseconds = 2000;

    private (IUIAutomation Automation, IUIAutomationCondition IsButton, IUIAutomationCacheRequest Bounds)? _uia;

    /// <returns>
    /// The rightmost button's right edge in physical screen pixels, 0 without any button; null without a taskbar.
    /// </returns>
    public int? RightEdge()
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
        {
            return null;
        }

        var (automation, isButton, bounds) = _uia ??= CreateAutomation();

        // Each element and array holds a reference into Explorer until it is released; a tray app collects
        // garbage too rarely to leave that to the finalizer
        IUIAutomationElement? root = null;
        IUIAutomationElementArray? buttons = null;
        try
        {
            root = automation.ElementFromHandle(new HWND(taskbar));

            // The bounds come back with the search, instead of one more call into Explorer per button
            buttons = root.FindAllBuildCache(TreeScope.TreeScope_Descendants, isButton, bounds);
            var right = 0;
            for (var index = 0; index < buttons.Length; index++)
            {
                var button = buttons.GetElement(index);
                try
                {
                    right = Math.Max(right, button.CachedBoundingRectangle.right);
                }
                finally
                {
                    Marshal.ReleaseComObject(button);
                }
            }

            return right;
        }
        finally
        {
            if (buttons is not null)
            {
                Marshal.ReleaseComObject(buttons);
            }

            if (root is not null)
            {
                Marshal.ReleaseComObject(root);
            }
        }
    }

    private static (IUIAutomation, IUIAutomationCondition, IUIAutomationCacheRequest) CreateAutomation()
    {
        var automation = (IUIAutomation2)new CUIAutomation8();
        automation.ConnectionTimeout = TimeoutMilliseconds;
        automation.TransactionTimeout = TimeoutMilliseconds;

        var isButton = automation.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ClassNamePropertyId, ButtonClass);

        // Only the cached rectangle is read; without a live element nothing else stays referenced in Explorer
        var bounds = automation.CreateCacheRequest();
        bounds.AddProperty(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId);
        bounds.AutomationElementMode = AutomationElementMode.AutomationElementMode_None;
        return (automation, isButton, bounds);
    }
}
