namespace Jelling.Core.Tests;

public class TaskbarPlacementTests
{
    private static readonly PixelRect Taskbar = new(0, 1032, 1920, 48);
    private static readonly PixelRect NotificationArea = new(1603, 1032, 317, 48);
    private static readonly PixelRect TaskList = new(55, 1032, 352, 48);

    [Fact]
    public void SitsLeftOfNotificationAreaRelativeToTaskbar()
    {
        var bounds = TaskbarPlacement.Calculate(Taskbar, NotificationArea, TaskList, taskButtonsRight: 0, width: 120);

        Assert.Equal(new PixelRect(1483, 0, 120, 48), bounds);
    }

    [Fact]
    public void FollowsTaskbarOnSecondaryMonitor()
    {
        var taskbar = new PixelRect(1920, 1368, 2560, 72);
        var notificationArea = new PixelRect(4000, 1368, 480, 72);
        var taskList = new PixelRect(2000, 1368, 500, 72);

        var bounds = TaskbarPlacement.Calculate(taskbar, notificationArea, taskList, taskButtonsRight: 0, width: 180);

        Assert.Equal(new PixelRect(1900, 0, 180, 72), bounds);
    }

    [Fact]
    public void NoRoomWhenItWouldCoverTheAppButtons()
    {
        var crowded = new PixelRect(55, 1032, 1500, 48);

        Assert.Null(TaskbarPlacement.Calculate(Taskbar, NotificationArea, crowded, taskButtonsRight: 0, width: 120));
    }

    [Fact]
    public void FitsWhenEndingExactlyAtTheAppButtons()
    {
        var taskList = new PixelRect(55, 1032, 1428, 48);

        Assert.NotNull(TaskbarPlacement.Calculate(Taskbar, NotificationArea, taskList, taskButtonsRight: 0, width: 120));
    }

    [Fact]
    public void NoRoomOnVerticalTaskbar()
    {
        // The tray is inset so that only the shape rules it out, not the taskbar's left edge
        var vertical = new PixelRect(0, 0, 62, 1080);
        var notificationArea = new PixelRect(40, 900, 22, 180);
        var taskList = new PixelRect(0, 0, 10, 400);

        Assert.Null(TaskbarPlacement.Calculate(vertical, notificationArea, taskList, taskButtonsRight: 0, width: 20));
    }

    [Fact]
    public void FitsWhenStartingExactlyAtTheTaskbarsLeftEdge()
    {
        var taskbar = new PixelRect(1920, 1368, 2560, 72);
        var notificationArea = new PixelRect(2040, 1368, 2440, 72);
        var emptyTaskList = new PixelRect(0, 0, 0, 0);

        var bounds = TaskbarPlacement.Calculate(taskbar, notificationArea, emptyTaskList, taskButtonsRight: 0, width: 120);

        Assert.Equal(new PixelRect(0, 0, 120, 72), bounds);
    }

    [Fact]
    public void NoRoomBeyondTheTaskbarsLeftEdge()
    {
        var taskbar = new PixelRect(1920, 1368, 2560, 72);
        var notificationArea = new PixelRect(1980, 1368, 2500, 72);
        var emptyTaskList = new PixelRect(0, 0, 0, 0);

        Assert.Null(TaskbarPlacement.Calculate(taskbar, notificationArea, emptyTaskList, taskButtonsRight: 0, width: 120));
    }

    [Fact]
    public void NoRoomWhenTaskButtonsReachPastTheStaleTaskList()
    {
        // Windows 11: the legacy task list window keeps its width while the XAML buttons grow to the tray
        var taskbar = new PixelRect(0, 1032, 1920, 48);
        var notificationArea = new PixelRect(1667, 1032, 253, 48);
        var staleTaskList = new PixelRect(55, 1032, 792, 48);

        Assert.Null(TaskbarPlacement.Calculate(taskbar, notificationArea, staleTaskList, taskButtonsRight: 1656, width: 96));
    }

    [Fact]
    public void NoRoomWhileTaskButtonsAreUnknown()
    {
        Assert.Null(TaskbarPlacement.Calculate(Taskbar, NotificationArea, TaskList, taskButtonsRight: null, width: 120));
    }

    [Fact]
    public void FitsWhenTaskButtonsEndExactlyAtTheElement()
    {
        var bounds = TaskbarPlacement.Calculate(Taskbar, NotificationArea, TaskList, taskButtonsRight: 1483, width: 120);

        Assert.Equal(new PixelRect(1483, 0, 120, 48), bounds);
    }
}
