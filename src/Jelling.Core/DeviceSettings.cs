using System.Text.Json;

namespace Jelling.Core;

/// <summary>
/// What the user set: per device whether it gets its own taskbar icon and its low threshold, and for all
/// devices whether the taskbar shows the level as a number. Stored as JSON; a missing or unreadable file
/// means nothing pinned, default thresholds and numbers shown; one that is no valid JSON is kept aside
/// as settings.invalid.json rather than overwritten.
/// </summary>
public sealed class DeviceSettings
{
    private readonly string _path;
    private readonly HashSet<Guid> _pinned;
    private readonly Dictionary<Guid, int> _lowThresholds;

    public DeviceSettings(string path)
    {
        _path = path;
        var stored = Read(path);
        _pinned = [.. stored?.PinnedDevices ?? []];
        ShowPercent = stored?.ShowPercent ?? true;

        // A hand-edited value outside the offered ones would leave the flyout's selection empty
        _lowThresholds = (stored?.LowThresholds ?? [])
            .Where(entry => BatteryLevel.IsOfferedThreshold(entry.Value))
            .ToDictionary();
    }

    public bool ShowPercent { get; private set; }

    public bool IsPinned(Guid deviceId) => _pinned.Contains(deviceId);

    public int LowThreshold(Guid deviceId) =>
        _lowThresholds.GetValueOrDefault(deviceId, BatteryLevel.DefaultLowThreshold);

    public void TogglePin(Guid deviceId)
    {
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

        _lowThresholds[deviceId] = threshold;
        Save();
    }

    public void SetShowPercent(bool show)
    {
        ShowPercent = show;
        Save();
    }

    private void Save()
    {
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

    private static Stored? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
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
