using System.Text.Json;

namespace Jelling.Core;

/// <summary>
/// What the user set: per device whether it gets its own taskbar icon and its low threshold, and for all
/// devices whether the taskbar shows the level as a number. Stored as JSON; a missing file means nothing
/// pinned, default thresholds and numbers shown; one that is no valid JSON is kept aside as
/// settings.invalid.json rather than overwritten. One that exists but cannot be read (locked, no access)
/// reads as defaults too, but is never overwritten: each change reads it again first, and saving throws
/// while it still cannot be read.
/// </summary>
public sealed class DeviceSettings
{
    private readonly string _path;
    private readonly HashSet<Guid> _pinned = [];
    private readonly Dictionary<Guid, int> _lowThresholds = [];

    // The file holds settings this instance never saw; writing the defaults over them would lose them
    private bool _readFailed;

    public DeviceSettings(string path)
    {
        _path = path;
        _readFailed = !TryLoad();
    }

    public bool ShowPercent { get; private set; } = true;

    public bool IsPinned(Guid deviceId) => _pinned.Contains(deviceId);

    public int LowThreshold(Guid deviceId) =>
        _lowThresholds.GetValueOrDefault(deviceId, BatteryLevel.DefaultLowThreshold);

    public void TogglePin(Guid deviceId)
    {
        ReloadIfReadFailed();
        if (!_pinned.Remove(deviceId))
        {
            _pinned.Add(deviceId);
        }

        Save();
    }

    /// <param name="threshold">One of <see cref="BatteryLevel.LowThresholds"/>.</param>
    public void SetLowThreshold(Guid deviceId, int threshold)
    {
        if (!BatteryLevel.IsOfferedThreshold(threshold))
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "Not one of the offered thresholds");
        }

        ReloadIfReadFailed();
        _lowThresholds[deviceId] = threshold;
        Save();
    }

    public void SetShowPercent(bool show)
    {
        ReloadIfReadFailed();
        ShowPercent = show;
        Save();
    }

    // E.g. a virus scanner held the file at startup; once it can be read, the change builds on what it holds
    private void ReloadIfReadFailed()
    {
        if (_readFailed)
        {
            _readFailed = !TryLoad();
        }
    }

    // False leaves the current values; the file exists but cannot be read
    private bool TryLoad()
    {
        var stored = Read(_path, out var failed);
        if (failed)
        {
            return false;
        }

        _pinned.Clear();
        _pinned.UnionWith(stored?.PinnedDevices ?? []);
        ShowPercent = stored?.ShowPercent ?? true;

        // A hand-edited value outside the offered ones would leave the flyout's selection empty
        _lowThresholds.Clear();
        foreach (var (deviceId, threshold) in stored?.LowThresholds ?? [])
        {
            if (BatteryLevel.IsOfferedThreshold(threshold))
            {
                _lowThresholds[deviceId] = threshold;
            }
        }

        return true;
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
            JsonSerializer.Serialize(stream, new Stored([.. _pinned], _lowThresholds, ShowPercent));
            stream.Flush(flushToDisk: true);
        }

        File.Move(pending, _path, overwrite: true);
    }

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
    private sealed record Stored(Guid[]? PinnedDevices, Dictionary<Guid, int>? LowThresholds, bool? ShowPercent);
}
