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
    // Size is the pixel size the icon was rendered at; a changed display scale needs a new image
    private readonly Dictionary<Guid, (TaskbarIcon Icon, DeviceBadge Badge, int Size)> _icons = [];

    // Icons whose NotifyIconSettings entry Explorer had not written yet when they were added
    private readonly HashSet<Guid> _unpromoted = [];

    // A tray that keeps refusing the icon would otherwise be logged on every refresh
    private string? _lastError;

    /// <param name="badges">From <see cref="DeviceBadge.ForPinned"/>; empty while the taskbar element shows them.</param>
    public void Update(IReadOnlyList<DeviceBadge> badges)
    {
        _unpromoted.RemoveWhere(TrayPromotion.TryPromote);

        foreach (var id in _icons.Keys.Except(badges.Select(b => b.Id)).ToList())
        {
            Remove(id);
        }

        // Once no tray icons are needed (nothing pinned, or the taskbar element shows them), the next failure,
        // maybe days later, is a new one worth logging
        if (badges.Count == 0)
        {
            _lastError = null;
        }

        var size = TrayIconRenderer.SmallIconSize();
        foreach (var badge in badges)
        {
            if (_icons.TryGetValue(badge.Id, out var current))
            {
                if (current.Badge != badge || current.Size != size)
                {
                    // The tooltip first: should it throw, no new image has been rendered that nothing owns.
                    // TaskbarIcon disposes the image it replaces, and its own on Dispose
                    current.Icon.ToolTipText = badge.ToolTip;
                    current.Icon.Icon = Render(badge, size);
                    _icons[badge.Id] = (current.Icon, badge, size);
                }

                continue;
            }

            Add(badge, size);
        }
    }

    public void Dispose()
    {
        foreach (var id in _icons.Keys.ToList())
        {
            Remove(id);
        }
    }

    private void Add(DeviceBadge badge, int size)
    {
        var image = Render(badge, size);
        TaskbarIcon icon;
        try
        {
            icon = new TaskbarIcon
            {
                // Must be set before ForceCreate
                Id = DeviceIconId.For(Environment.ProcessPath!, badge.Id),
                Icon = image,
                ToolTipText = badge.ToolTip,
                NoLeftClickDelay = true,
                LeftClickCommand = new RelayCommand(onClick),
            };
        }
        catch
        {
            // No icon holds the image yet, so disposing the icon below would not reach it
            image.Dispose();
            throw;
        }

        try
        {
            icon.ForceCreate(false);
            _lastError = null;
            _icons[badge.Id] = (icon, badge, size);
            if (!TrayPromotion.TryPromote(icon.Id))
            {
                _unpromoted.Add(icon.Id);
            }
        }
        catch (Exception exception)
        {
            // E.g. while Explorer restarts; the next refresh tries again instead of piling up icons
            var error = $"{exception.GetType()}: {exception.Message}";
            if (error != _lastError)
            {
                CrashLog.Write("DeviceTrayIcon", exception);
                _lastError = error;
            }

            icon.Dispose();
        }
    }

    private void Remove(Guid deviceId)
    {
        var icon = _icons[deviceId].Icon;
        _unpromoted.Remove(icon.Id);
        icon.Dispose();
        _icons.Remove(deviceId);
    }

    private static Icon Render(DeviceBadge badge, int size) =>
        TrayIconRenderer.RenderDevice(badge.Glyph, badge.Percent, badge.GlyphColor, badge.LevelColor, size);
}
