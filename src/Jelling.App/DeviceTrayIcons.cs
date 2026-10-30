using System.Drawing;
using Jelling.Core;
using H.NotifyIcon;

namespace Jelling.App;

/// <summary>
/// One extra tray icon per pinned, connected device: its glyph above the battery level, or the glyph alone
/// in the level's color, shown in the taskbar rather than in the overflow.
/// </summary>
public sealed class DeviceTrayIcons(Action onClick) : IDisposable
{
    private readonly Dictionary<Guid, (TaskbarIcon Icon, DeviceBadge Badge)> _icons = [];

    // Icons whose NotifyIconSettings entry Explorer had not written yet when they were added
    private readonly HashSet<Guid> _unpromoted = [];

    /// <param name="badges">From <see cref="DeviceBadge.ForPinned"/>; empty while the taskbar element shows them.</param>
    public void Update(IReadOnlyList<DeviceBadge> badges)
    {
        _unpromoted.RemoveWhere(TrayPromotion.TryPromote);

        foreach (var id in _icons.Keys.Except(badges.Select(b => b.Id)).ToList())
        {
            Remove(id);
        }

        foreach (var badge in badges)
        {
            if (_icons.TryGetValue(badge.Id, out var current))
            {
                if (current.Badge != badge)
                {
                    // The shell copies the image on update, so the previous one can be released
                    var previous = current.Icon.Icon;
                    current.Icon.Icon = Render(badge);
                    current.Icon.ToolTipText = badge.ToolTip;
                    previous?.Dispose();
                    _icons[badge.Id] = (current.Icon, badge);
                }

                continue;
            }

            Add(badge);
        }
    }

    public void Dispose()
    {
        foreach (var id in _icons.Keys.ToList())
        {
            Remove(id);
        }
    }

    private void Add(DeviceBadge badge)
    {
        var icon = new TaskbarIcon
        {
            // Must be set before ForceCreate
            Id = DeviceIconId.For(Environment.ProcessPath!, badge.Id),
            Icon = Render(badge),
            ToolTipText = badge.ToolTip,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(onClick),
        };

        try
        {
            icon.ForceCreate(false);
            _icons[badge.Id] = (icon, badge);
            if (!TrayPromotion.TryPromote(icon.Id))
            {
                _unpromoted.Add(icon.Id);
            }
        }
        catch (Exception exception)
        {
            // E.g. while Explorer restarts; the next refresh tries again instead of piling up icons
            CrashLog.Write("DeviceTrayIcon", exception);
            Release(icon);
        }
    }

    private void Remove(Guid deviceId)
    {
        var icon = _icons[deviceId].Icon;
        _unpromoted.Remove(icon.Id);
        Release(icon);
        _icons.Remove(deviceId);
    }

    // TaskbarIcon does not own the image it shows
    private static void Release(TaskbarIcon icon)
    {
        var image = icon.Icon;
        icon.Dispose();
        image?.Dispose();
    }

    private static Icon Render(DeviceBadge badge) =>
        TrayIconRenderer.RenderDevice(badge.Glyph, badge.Percent, badge.GlyphColor, badge.LevelColor);
}
