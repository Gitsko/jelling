using Jelling.Core;
using Microsoft.UI.Xaml.Controls;

namespace Jelling.App;

/// <summary>The tray icon's right-click menu.</summary>
internal static class TrayMenu
{
    /// <param name="settings">What the ticks read back after each change, so they show what is stored.</param>
    /// <param name="setLevelDisplay">Saves the setting and redraws.</param>
    /// <param name="setShowWidget">Saves the setting and shows or removes the widget.</param>
    /// <param name="setWidgetLevelDisplay">Saves the setting and redraws the widget.</param>
    /// <param name="setWidgetShowNames">Saves the setting and redraws the widget.</param>
    /// <param name="setWidgetScale">Saves the setting and redraws the widget.</param>
    /// <param name="setWidgetLocked">Saves the setting.</param>
    /// <param name="guard">Runs an action so that its failure is logged instead of ending the app.</param>
    public static MenuFlyout Create(
        string title, DeviceSettings settings, Action<LevelDisplay> setLevelDisplay, Action<bool> setShowWidget,
        Action<LevelDisplay> setWidgetLevelDisplay, Action<bool> setWidgetShowNames, Action<int> setWidgetScale,
        Action<bool> setWidgetLocked, Action exit, Action<string, Action> guard)
    {
        // PopupMenu mode turns the flyout into a native menu that only runs Command, not Click
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = title, IsEnabled = false });
        menu.Items.Add(new MenuFlyoutSeparator());

        var autoStart = new ToggleMenuFlyoutItem { Text = Strings.Get("AutoStart"), IsChecked = AutoStart.IsEnabled };
        // The native menu does not flip IsChecked itself; read the setting back so the tick shows what is stored
        // A policy or security tool may lock the Run key or the startup task; the tick then stays as it was
        autoStart.Command = new RelayCommand(() => guard("AutoStart", () =>
        {
            AutoStart.Set(!AutoStart.IsEnabled);
            autoStart.IsChecked = AutoStart.IsEnabled;
        }));
        menu.Items.Add(autoStart);

        menu.Items.Add(Choice(Strings.Get("LevelDisplay"), () => settings.LevelDisplay, setLevelDisplay, LevelDisplays()));

        // The widget's own choices change nothing while it is off, so they are greyed out until it is on
        MenuFlyoutItemBase[] widgetItems =
        [
            Choice(Strings.Get("WidgetLevelDisplay"), () => settings.WidgetLevelDisplay, setWidgetLevelDisplay, LevelDisplays()),
            Toggle(Strings.Get("WidgetShowNames"), () => settings.WidgetShowNames, setWidgetShowNames),
            Choice(Strings.Get("WidgetScale"), () => settings.WidgetScale, setWidgetScale,
                [.. DeviceSettings.WidgetScales.Select(scale => (scale, $"{scale}\u00D7"))]),
            Toggle(Strings.Get("LockWidget"), () => settings.WidgetLocked, setWidgetLocked),
        ];
        void EnableWidgetItems()
        {
            foreach (var item in widgetItems)
            {
                item.IsEnabled = settings.ShowWidget;
            }
        }

        menu.Items.Add(Toggle(Strings.Get("ShowWidget"), () => settings.ShowWidget, show =>
        {
            setShowWidget(show);
            EnableWidgetItems();
        }));
        foreach (var item in widgetItems)
        {
            menu.Items.Add(item);
        }

        EnableWidgetItems();
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = Strings.Get("Exit"), Command = new RelayCommand(exit) });
        return menu;
    }

    private static IReadOnlyList<(LevelDisplay Value, string Text)> LevelDisplays() =>
    [
        (LevelDisplay.Number, Strings.Get("LevelDisplayNumber")),
        (LevelDisplay.Ring, Strings.Get("LevelDisplayRing")),
        (LevelDisplay.Symbol, Strings.Get("LevelDisplaySymbol")),
    ];

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
                // Picking the option already ticked changes nothing and need not rewrite the file
                if (!EqualityComparer<T>.Default.Equals(get(), value))
                {
                    set(value);
                }

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
