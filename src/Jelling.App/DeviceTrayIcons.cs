using System.Drawing;
using Jelling.Core;
using H.NotifyIcon;

namespace Jelling.App;

/// <summary>
/// One extra tray icon per pinned, connected device: its glyph above the battery level, or the glyph alone
/// in the level's color, shown in the taskbar rather than in the overflow. A faint line right of them sets them
/// apart from the system icons; the tray leaves no room between icons to draw it in.
/// </summary>
public sealed class DeviceTrayIcons(Action onClick) : IDisposable
{
    // Size is the pixel size the icon was rendered at; a changed display scale needs a new image
    private readonly Dictionary<Guid, (TaskbarIcon Icon, DeviceBadge Badge, int Size)> _icons = [];

    // Icons whose NotifyIconSettings entry Explorer had not written yet when they were added
    private readonly HashSet<Guid> _unpromoted = [];

    // A tray that keeps refusing the icon would otherwise be logged on every refresh
    private string? _lastError;

    private (TaskbarIcon Icon, DeviceColors Colors, int Size)? _separator;

    /// <param name="badges">From <see cref="DeviceBadge.ForPinned"/>; empty while the taskbar element shows them.</param>
    /// <param name="colors">The taskbar's device colors, for the separator.</param>
    public void Update(IReadOnlyList<DeviceBadge> badges, DeviceColors colors)
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

        // Explorer puts a new icon left of the ones it knows and keeps a place the user dragged it to by its GUID.
        // Created before the device icons, the separator ends up right of them on a fresh install. An install
        // over a version without it already knows the device icons, so the separator lands left of them until
        // dragged once
        var size = TrayIconRenderer.SmallIconSize();
        if (badges.Count > 0)
        {
            ShowSeparator(colors, size);
        }
        else
        {
            RemoveSeparator();
        }

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

            // Added only after the separator, or it would go left of them for good. A refused separator is tried
            // again on the next refresh, the device icons with it
            if (_separator is not null)
            {
                Add(badge, size);
            }
        }
    }

    public void Dispose()
    {
        RemoveSeparator();
        foreach (var id in _icons.Keys.ToList())
        {
            Remove(id);
        }
    }

    private void Add(DeviceBadge badge, int size)
    {
        var icon = Create(DeviceIconId.For(Environment.ProcessPath!, badge.Id), Render(badge, size), badge.ToolTip);
        if (icon is not null)
        {
            _icons[badge.Id] = (icon, badge, size);
        }
    }

    private void ShowSeparator(DeviceColors colors, int size)
    {
        if (_separator is { } current)
        {
            if (current.Colors != colors || current.Size != size)
            {
                current.Icon.Icon = RenderSeparator(colors, size);
                _separator = (current.Icon, colors, size);
            }

            return;
        }

        // Named after the app, so the entry in the taskbar settings says whose it is
        var icon = Create(DeviceIconId.Separator(Environment.ProcessPath!), RenderSeparator(colors, size), Strings.Get("SeparatorToolTip"));
        if (icon is not null)
        {
            _separator = (icon, colors, size);
        }
    }

    private void RemoveSeparator()
    {
        if (_separator is { } current)
        {
            Destroy(current.Icon);
            _separator = null;
        }
    }

    // Null when the tray refused the icon; the next refresh tries again
    private TaskbarIcon? Create(Guid id, Icon image, string toolTip)
    {
        TaskbarIcon icon;
        try
        {
            icon = new TaskbarIcon
            {
                // Must be set before ForceCreate
                Id = id,
                Icon = image,
                ToolTipText = toolTip,
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
            if (!TrayPromotion.TryPromote(icon.Id))
            {
                _unpromoted.Add(icon.Id);
            }

            return icon;
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
            return null;
        }
    }

    private void Remove(Guid deviceId)
    {
        Destroy(_icons[deviceId].Icon);
        _icons.Remove(deviceId);
    }

    private void Destroy(TaskbarIcon icon)
    {
        _unpromoted.Remove(icon.Id);
        icon.Dispose();
    }

    // High contrast has no faded colors, so the line takes the full text color there
    private static Icon RenderSeparator(DeviceColors colors, int size) =>
        TrayIconRenderer.RenderSeparator(colors.Normal, faded: !colors.IsHighContrast, size);

    private static Icon Render(DeviceBadge badge, int size) =>
        TrayIconRenderer.RenderDevice(
            badge.Glyph, badge.Percent, badge.GlyphColor, badge.LevelColor, badge.RingPercent, badge.RingTrack, size);
}
