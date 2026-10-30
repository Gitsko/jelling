using System.Collections.Immutable;

namespace Jelling.Core;

public static class BatteryLevel
{
    /// <summary>The low threshold of a device the user has not set one for.</summary>
    public const int DefaultLowThreshold = 20;

    /// <summary>The thresholds a device can be set to; 0 turns the low warning off.</summary>
    public static ImmutableArray<int> LowThresholds { get; } = [0, 10, 15, 20, 25, 30, 40, 50];

    public static bool IsOfferedThreshold(int threshold) => LowThresholds.Contains(threshold);

    /// <param name="threshold">At or below this percentage the device counts as low; 0 means never.</param>
    public static bool IsLow(int percent, int threshold) => threshold > 0 && percent <= threshold;

    /// <summary>
    /// Maps a percentage to one of eleven steps (0-10), matching the Segoe Fluent battery glyphs.
    /// </summary>
    public static int ToStep(int percent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(percent, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);

        return (int)Math.Round(percent / 10.0, MidpointRounding.AwayFromZero);
    }
}
