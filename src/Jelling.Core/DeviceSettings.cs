using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jelling.Core;

/// <summary>
/// What the user set: per device whether it shows in the taskbar, whether on the widget, and its low
/// threshold, for all devices how the level shows in the taskbar and on the widget, and whether, where,
/// how large and with or without names the desktop widget shows. Stored as JSON; a missing file means
/// nothing pinned, nothing on the widget, default thresholds, numbers shown and no widget. A file from
/// before the widget had its own choices puts the pinned devices on it and shows the level there as in
/// the taskbar. Fields this version does not know, written by a newer one, are kept when it saves. One
/// that is no valid JSON is kept aside as settings.invalid.json rather than overwritten. One that exists
/// but cannot be read (locked, no access) reads as defaults too, but is never overwritten: each change
/// reads it again first, and saving throws
/// while it still cannot be read. Changes made meanwhile still apply, and once it reads they are applied
/// again on top of what it holds.
/// </summary>
public sealed class DeviceSettings
{
    private readonly string _path;
    private readonly HashSet<Guid> _pinned = [];
    private readonly HashSet<Guid> _widget = [];
    private readonly Dictionary<Guid, int> _lowThresholds = [];

    // What a newer version stored and this one does not know; written back as read
    private Dictionary<string, JsonElement>? _unknown;

    // The file holds settings this instance never saw; writing the defaults over them would lose them
    private bool _readFailed;

    // Changes made while _readFailed, applied again once the file reads; each sets a value, so applying
    // it twice changes nothing
    private readonly List<Action> _unsaved = [];

    public DeviceSettings(string path)
    {
        _path = path;
        Load();
    }

    /// <summary>The sizes the widget is offered in, as multiples of its normal size.</summary>
    public static IReadOnlyList<int> WidgetScales { get; } = [1, 2, 3, 4];

    /// <summary>How the level shows in the taskbar.</summary>
    public LevelDisplay LevelDisplay { get; private set; }

    public LevelDisplay WidgetLevelDisplay { get; private set; }

    public bool ShowWidget { get; private set; }

    /// <summary>True when the widget stays where it is instead of moving with a drag.</summary>
    public bool WidgetLocked { get; private set; }

    /// <summary>The widget's top left corner in physical screen pixels; null until it was first moved.</summary>
    public PixelPoint? WidgetPosition { get; private set; }

    /// <summary>True when the widget names each device below it.</summary>
    public bool WidgetShowNames { get; private set; }

    /// <summary>One of <see cref="WidgetScales"/>.</summary>
    public int WidgetScale { get; private set; } = 1;

    public bool IsPinned(Guid deviceId) => _pinned.Contains(deviceId);

    public bool IsOnWidget(Guid deviceId) => _widget.Contains(deviceId);

    public int LowThreshold(Guid deviceId) =>
        _lowThresholds.GetValueOrDefault(deviceId, BatteryLevel.DefaultLowThreshold);

    /// <param name="pinned">What the user chose, not a toggle: applied again after a reload it must not flip back.</param>
    public void SetPinned(Guid deviceId, bool pinned) => Apply(() => Set(_pinned, deviceId, pinned));

    /// <param name="onWidget">What the user chose, like <see cref="SetPinned"/>.</param>
    public void SetOnWidget(Guid deviceId, bool onWidget) => Apply(() => Set(_widget, deviceId, onWidget));

    // The opposite of what the user sees now, read before a reload that could change it
    public void TogglePin(Guid deviceId) => SetPinned(deviceId, !IsPinned(deviceId));

    public void ToggleWidget(Guid deviceId) => SetOnWidget(deviceId, !IsOnWidget(deviceId));

    /// <param name="threshold">One of <see cref="BatteryLevel.LowThresholds"/>.</param>
    public void SetLowThreshold(Guid deviceId, int threshold)
    {
        if (!BatteryLevel.IsOfferedThreshold(threshold))
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "Not one of the offered thresholds");
        }

        Apply(() => _lowThresholds[deviceId] = threshold);
    }

    public void SetLevelDisplay(LevelDisplay display) => Apply(() => LevelDisplay = display);

    public void SetWidgetLevelDisplay(LevelDisplay display) => Apply(() => WidgetLevelDisplay = display);

    public void SetShowWidget(bool show) => Apply(() => ShowWidget = show);

    public void SetWidgetShowNames(bool show) => Apply(() => WidgetShowNames = show);

    public void SetWidgetLocked(bool locked) => Apply(() => WidgetLocked = locked);

    public void SetWidgetPosition(PixelPoint position) => Apply(() => WidgetPosition = position);

    /// <param name="scale">One of <see cref="WidgetScales"/>.</param>
    public void SetWidgetScale(int scale)
    {
        if (!WidgetScales.Contains(scale))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Not one of the offered widget sizes");
        }

        Apply(() => WidgetScale = scale);
    }

    private static void Set(HashSet<Guid> devices, Guid deviceId, bool shown)
    {
        if (shown)
        {
            devices.Add(deviceId);
        }
        else
        {
            devices.Remove(deviceId);
        }
    }

    // E.g. a virus scanner held the file at startup; once it can be read, the change builds on what it holds
    private void ReloadIfReadFailed()
    {
        if (!_readFailed)
        {
            return;
        }

        Load();
        if (!_readFailed)
        {
            _unsaved.ForEach(change => change());
            _unsaved.Clear();
        }
    }

    // Each change sets a value, so it can build on what a reload brought in and be applied again later
    private void Apply(Action change)
    {
        ReloadIfReadFailed();
        change();
        if (_readFailed)
        {
            _unsaved.Add(change);
        }

        Save();
    }

    // A file that exists but cannot be read leaves the current values
    private void Load()
    {
        var stored = Read(_path, out _readFailed);
        if (_readFailed)
        {
            return;
        }

        _unknown = stored?.Unknown;
        _pinned.Clear();
        _pinned.UnionWith(stored?.PinnedDevices ?? []);

        // Only a missing list falls back to the pins; an empty one means the user took every device off
        _widget.Clear();
        _widget.UnionWith(stored?.WidgetDevices ?? stored?.PinnedDevices ?? []);
        LevelDisplay = ParseLevelDisplay(stored?.LevelDisplay)
            ?? (stored?.LevelDisplay is null && stored?.ShowPercent == false ? LevelDisplay.Symbol : LevelDisplay.Number);
        WidgetLevelDisplay = ParseLevelDisplay(stored?.WidgetLevelDisplay) ?? LevelDisplay;
        ShowWidget = stored?.ShowWidget ?? false;
        WidgetLocked = stored?.WidgetLocked ?? false;
        WidgetShowNames = stored?.WidgetShowNames ?? false;
        WidgetPosition = stored?.WidgetPosition;
        WidgetScale = stored?.WidgetScale is { } scale && WidgetScales.Contains(scale) ? scale : 1;

        // A hand-edited value outside the offered ones would leave the flyout's selection empty
        _lowThresholds.Clear();
        foreach (var (deviceId, threshold) in stored?.LowThresholds ?? [])
        {
            if (BatteryLevel.IsOfferedThreshold(threshold))
            {
                _lowThresholds[deviceId] = threshold;
            }
        }
    }

    private void Save()
    {
        if (_readFailed)
        {
            throw new IOException($"{_path} could not be read; not overwriting it");
        }

        // Write aside, flush to disk and swap, so neither a crash nor a power loss mid-write can leave
        // a truncated file that loses all settings
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var pending = _path + ".pending";
        using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write))
        {
            // ShowPercent keeps an older version, after a downgrade, as close as it gets to the chosen display
            JsonSerializer.Serialize(stream, new Stored(
                PinnedDevices: [.. _pinned],
                LowThresholds: _lowThresholds,
                ShowPercent: LevelDisplay == LevelDisplay.Number,
                ShowWidget: ShowWidget,
                WidgetLocked: WidgetLocked,
                WidgetPosition: WidgetPosition,
                LevelDisplay: LevelDisplay.ToString(),
                WidgetScale: WidgetScale,
                WidgetDevices: [.. _widget],
                WidgetLevelDisplay: WidgetLevelDisplay.ToString(),
                WidgetShowNames: WidgetShowNames)
            {
                Unknown = _unknown,
            });
            stream.Flush(flushToDisk: true);
        }

        File.Move(pending, _path, overwrite: true);
    }

    // Stored as its name: an enum the serializer parses would throw for a hand-edited value and cost the whole
    // file. Null for a missing or unknown one; files from before the choice only know whether the number showed
    private static LevelDisplay? ParseLevelDisplay(string? stored) =>
        Enum.TryParse<LevelDisplay>(stored, ignoreCase: true, out var display)
        && Enum.IsDefined(display)
        && !int.TryParse(stored, out _)
            ? display
            : null;

    private static Stored? Read(string path, out bool failed)
    {
        failed = false;
        try
        {
            return JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failed = true;
            return null;
        }
        catch (JsonException)
        {
            // The next save would overwrite it; kept aside, a hand-edited file can still be repaired
            try
            {
                File.Move(path, Path.ChangeExtension(path, ".invalid.json"), overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Still read as defaults; only keeping the copy failed
            }

            return null;
        }
    }

    // Older files lack the later fields; they then read as null
    private sealed record Stored(
        Guid[]? PinnedDevices, Dictionary<Guid, int>? LowThresholds, bool? ShowPercent,
        bool? ShowWidget, bool? WidgetLocked, PixelPoint? WidgetPosition, string? LevelDisplay, int? WidgetScale,
        Guid[]? WidgetDevices, string? WidgetLevelDisplay, bool? WidgetShowNames)
    {
        // A newer version's fields; the serializer does not bind extension data to a constructor parameter
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Unknown { get; init; }
    }
}
