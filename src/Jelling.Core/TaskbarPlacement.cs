namespace Jelling.Core;

/// <summary>
/// Computes where the device strip sits inside the taskbar, in physical pixels: directly left of the
/// notification area, as tall as it. Null means there is no room, and the app falls back to tray icons.
/// </summary>
public static class TaskbarPlacement
{
    /// <param name="taskbar">The taskbar window; the result is relative to it.</param>
    /// <param name="notificationArea">The tray, clock included; the strip ends at its left edge.</param>
    /// <param name="taskList">The app buttons' band; the strip must not cover it.</param>
    /// <param name="taskButtonsRight">
    /// The right edge of the rightmost app button, 0 without any; null while unknown, which means no room.
    /// On Windows 11 the task list window keeps its width while the buttons grow beyond it.
    /// </param>
    public static PixelRect? Calculate(PixelRect taskbar, PixelRect notificationArea, PixelRect taskList, int? taskButtonsRight, int width)
    {
        // Windows 11 only docks the taskbar at the bottom; anything taller than wide is not that taskbar
        if (taskbar.Width <= taskbar.Height)
        {
            return null;
        }

        if (taskButtonsRight is not { } buttonsRight)
        {
            return null;
        }

        var x = notificationArea.X - width;
        if (x < taskList.Right || x < buttonsRight || x < taskbar.X)
        {
            return null;
        }

        return new PixelRect(x - taskbar.X, notificationArea.Y - taskbar.Y, width, notificationArea.Height);
    }
}
