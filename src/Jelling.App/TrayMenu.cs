using Jelling.Core;
using Microsoft.UI.Xaml.Controls;

namespace Jelling.App;

/// <summary>The tray icon's right-click menu.</summary>
internal static class TrayMenu
{
    /// <param name="setLevelDisplay">Saves the setting and redraws; the ticks then read <paramref name="levelDisplay"/> back.</param>
    /// <param name="setShowWidget">Saves the setting and shows or removes the widget; the tick reads it back the same way.</param>
    /// <param name="setWidgetScale">Saves the setting and redraws the widget; the ticks read it back the same way.</param>
    /// <param name="setWidgetLocked">Saves the setting; the tick reads it back the same way.</param>
    /// <param name="guard">Runs an action so that its failure is logged instead of ending the app.</param>
    public static MenuFlyout Create(
        string title, Func<LevelDisplay> levelDisplay, Action<LevelDisplay> setLevelDisplay,
        Func<bool> showWidget, Action<bool> setShowWidget, Func<int> widgetScale, Action<int> setWidgetScale,
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

        menu.Items.Add(Choice(Strings.Get("LevelDisplay"), levelDisplay, setLevelDisplay, [
            (LevelDisplay.Number, Strings.Get("LevelDisplayNumber")),
            (LevelDisplay.Ring, Strings.Get("LevelDisplayRing")),
            (LevelDisplay.Symbol, Strings.Get("LevelDisplaySymbol")),
        ]));
        menu.Items.Add(Toggle(Strings.Get("ShowWidget"), showWidget, setShowWidget));
        menu.Items.Add(Choice(Strings.Get("WidgetScale"), widgetScale, setWidgetScale,
            [.. DeviceSettings.WidgetScales.Select(scale => (scale, $"{scale}\u00D7"))]));
        menu.Items.Add(Toggle(Strings.Get("LockWidget"), widgetLocked, setWidgetLocked));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = Strings.Get("Exit"), Command = new RelayCommand(exit) });
        return menu;
    }

    // RadioMenuFlyoutItem would only get its group behavior from XAML, which the native menu never runs; each
    // option's tick reads the setting back instead, so exactly the stored one shows
    private static MenuFlyoutSubItem Choice<T>(string text, Func<T> get, Action<T> set, IReadOnlyList<(T Value, string Text)> options)
    {
        var sub = new MenuFlyoutSubItem { Text = text };
        var items = new List<(T Value, ToggleMenuFlyoutItem Item)>();
        foreach (var (value, label) in options)
        {
            var item = new ToggleMenuFlyoutItem { Text = label, IsChecked = EqualityComparer<T>.Default.Equals(get(), value) };
            item.Command = new RelayCommand(() =>
            {
                set(value);
                foreach (var (option, other) in items)
                {
                    other.IsChecked = EqualityComparer<T>.Default.Equals(get(), option);
                }
            });
            items.Add((value, item));
            sub.Items.Add(item);
        }

        return sub;
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
