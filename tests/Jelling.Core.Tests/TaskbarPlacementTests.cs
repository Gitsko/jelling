namespace Jelling.Core.Tests;

public class TaskbarPlacementTests
{
    private static readonly PixelRect Taskbar = new(0, 1032, 1920, 48);
    private static readonly PixelRect NotificationArea = new(1603, 1032, 317, 48);
    private static readonly PixelRect TaskList = new(55, 1032, 352, 48);

    [Fact]
    public void SitsLeftOfNotificationAreaRelativeToTaskbar()
    {
        var bounds = TaskbarPlacement.Calculate(Taskbar, NotificationArea, TaskList, width: 120);

        Assert.Equal(new PixelRect(1483, 0, 120, 48), bounds);
    }

    [Fact]
    public void FollowsTaskbarOnSecondaryMonitor()
    {
        var taskbar = new PixelRect(1920, 1368, 2560, 72);
        var notificationArea = new PixelRect(4000, 1368, 480, 72);
        var taskList = new PixelRect(2000, 1368, 500, 72);

        var bounds = TaskbarPlacement.Calculate(taskbar, notificationArea, taskList, width: 180);

        Assert.Equal(new PixelRect(1900, 0, 180, 72), bounds);
    }

    [Fact]
    public void NoRoomWhenItWouldCoverTheAppButtons()
    {
        var crowded = new PixelRect(55, 1032, 1500, 48);

        Assert.Null(TaskbarPlacement.Calculate(Taskbar, NotificationArea, crowded, width: 120));
    }

    [Fact]
    public void FitsWhenEndingExactlyAtTheAppButtons()
    {
        var taskList = new PixelRect(55, 1032, 1428, 48);

        Assert.NotNull(TaskbarPlacement.Calculate(Taskbar, NotificationArea, taskList, width: 120));
    }

    [Fact]
    public void NoRoomOnVerticalTaskbar()
    {
        var vertical = new PixelRect(0, 0, 62, 1080);
        var notificationArea = new PixelRect(0, 900, 62, 180);
        var taskList = new PixelRect(0, 0, 62, 400);

        Assert.Null(TaskbarPlacement.Calculate(vertical, notificationArea, taskList, width: 50));
    }

    [Fact]
    public void NoRoomBeyondTheTaskbarsLeftEdge()
    {
        var taskbar = new PixelRect(1920, 1368, 2560, 72);
        var notificationArea = new PixelRect(1980, 1368, 2500, 72);
        var emptyTaskList = new PixelRect(0, 0, 0, 0);

        Assert.Null(TaskbarPlacement.Calculate(taskbar, notificationArea, emptyTaskList, width: 120));
    }
}
