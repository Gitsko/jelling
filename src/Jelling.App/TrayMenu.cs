using Microsoft.UI.Xaml.Controls;

namespace Jelling.App;

/// <summary>The tray icon's right-click menu.</summary>
internal static class TrayMenu
{
    /// <param name="setShowPercent">Saves the setting and redraws; the tick then reads <paramref name="showPercent"/> back.</param>
    /// <param name="setShowWidget">Saves the setting and shows or removes the widget; the tick reads it back the same way.</param>
    /// <param name="setWidgetLocked">Saves the setting; the tick reads it back the same way.</param>
    /// <param name="guard">Runs an action so that its failure is logged instead of ending the app.</param>
    public static MenuFlyout Create(
        string title, Func<bool> showPercent, Action<bool> setShowPercent, Func<bool> showWidget, Action<bool> setShowWidget,
        Func<bool> widgetLocked, Action<bool> setWidgetLocked, Action exit, Action<string, Action> guard)
    {
        // PopupMenu mode turns the flyout into a native menu that only runs Command, not Click
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = title, IsEnabled = false });
        menu.Items.Add(new MenuFlyoutSeparator());

        var autoStart = new ToggleMenuFlyoutItem { Text = Strings.Get("AutoStart"), IsChecked = AutoStart.IsEnabled };
        // The native menu does not flip IsChecked itself; read the registry back so the tick shows what is stored
        // A policy or security tool may lock the Run key; the tick then stays as it was
        autoStart.Command = new RelayCommand(() => guard("AutoStart", () =>
        {
            AutoStart.Set(!AutoStart.IsEnabled);
            autoStart.IsChecked = AutoStart.IsEnabled;
        }));
        menu.Items.Add(autoStart);

        var percent = new ToggleMenuFlyoutItem { Text = Strings.Get("ShowPercent"), IsChecked = showPercent() };
        percent.Command = new RelayCommand(() =>
        {
            setShowPercent(!showPercent());
            percent.IsChecked = showPercent();
        });
        menu.Items.Add(percent);
        menu.Items.Add(Toggle(Strings.Get("ShowWidget"), showWidget, setShowWidget));
        menu.Items.Add(Toggle(Strings.Get("LockWidget"), widgetLocked, setWidgetLocked));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = Strings.Get("Exit"), Command = new RelayCommand(exit) });
        return menu;
    }

    private static ToggleMenuFlyoutItem Toggle(string text, Func<bool> get, Action<bool> set)
    {
        var item = new ToggleMenuFlyoutItem { Text = text, IsChecked = get() };
        item.Command = new RelayCommand(() =>
        {
            set(!get());
            item.IsChecked = get();
        });
        return item;
    }
}
