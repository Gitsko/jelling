namespace Jelling.Core;

/// <summary>
/// Decides which devices get a low-battery warning: once per discharge, at the device's low threshold
/// or below. A device can only warn again after it has been charged more than <see cref="RearmMargin"/>
/// points above its threshold, so a level hovering around the threshold does not warn on every reading.
/// </summary>
public sealed class LowBatteryNotifier
{
    public const int RearmMargin = 10;

    private readonly HashSet<Guid> _warned = [];

    /// <param name="thresholdFor">The device's low threshold; 0 turns its warning off.</param>
    /// <returns>The devices that just became low and should be announced.</returns>
    public IReadOnlyList<BatteryDevice> Check(IEnumerable<BatteryDevice> devices, Func<Guid, int> thresholdFor)
    {
        var newlyLow = new List<BatteryDevice>();
        foreach (var device in devices)
        {
            // A disconnected device keeps its last level but no longer drains
            if (!device.IsConnected || device.BatteryPercent is not { } percent)
            {
                continue;
            }

            var threshold = thresholdFor(device.Id);
            if (percent > threshold + RearmMargin)
            {
                _warned.Remove(device.Id);
            }
            else if (BatteryLevel.IsLow(percent, threshold) && _warned.Add(device.Id))
            {
                newlyLow.Add(device);
            }
        }

        return newlyLow;
    }

    /// <summary>
    /// Forgets that the device was warned, for a changed threshold: a warning given at the old one
    /// must not swallow the first warning at the new one.
    /// </summary>
    public void Reset(Guid deviceId) => _warned.Remove(deviceId);
}
