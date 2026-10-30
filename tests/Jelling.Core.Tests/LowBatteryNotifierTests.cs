namespace Jelling.Core.Tests;

public class LowBatteryNotifierTests
{
    private static readonly Guid Headset = Guid.Parse("00000000-0000-5000-8000-000000000001");
    private static readonly Guid Mouse = Guid.Parse("00000000-0000-5000-8000-000000000002");

    private readonly LowBatteryNotifier _notifier = new();
    private readonly Dictionary<Guid, int> _thresholds = [];

    [Fact]
    public void WarnsOnceWhenADeviceDropsToLow()
    {
        Assert.Empty(Check(Device(Headset, 40)));
        Assert.Equal([Headset], Check(Device(Headset, 20)));
        Assert.Empty(Check(Device(Headset, 15)));
        Assert.Empty(Check(Device(Headset, 5)));
    }

    [Fact]
    public void WarnsExactlyAtTheThreshold()
    {
        Assert.Empty(Check(Device(Headset, 21)));
        Assert.Equal([Headset], Check(Device(Headset, 20)));
    }

    [Fact]
    public void WarnsWhenTheFirstReadingIsBetweenLowAndRearm()
    {
        Assert.Empty(Check(Device(Headset, 25)));
        Assert.Equal([Headset], Check(Device(Headset, 20)));
    }

    [Fact]
    public void DoesNotWarnAgainBeforeChargingPastTheRearmMargin()
    {
        Check(Device(Headset, 18));

        Assert.Empty(Check(Device(Headset, 25)));
        Assert.Empty(Check(Device(Headset, 30)));
        Assert.Empty(Check(Device(Headset, 19)));
    }

    [Fact]
    public void WarnsAgainAfterChargingPastTheRearmMargin()
    {
        Check(Device(Headset, 18));
        Check(Device(Headset, 31));

        Assert.Equal([Headset], Check(Device(Headset, 20)));
    }

    [Fact]
    public void WarnsAtStartWhenADeviceIsAlreadyLow()
    {
        Assert.Equal([Headset], Check(Device(Headset, 10)));
    }

    [Fact]
    public void TracksEachDeviceSeparately()
    {
        Check(Device(Headset, 10));

        Assert.Equal([Mouse], Check(Device(Headset, 9), Device(Mouse, 12)));
    }

    [Fact]
    public void IgnoresDevicesWithoutLevel()
    {
        Assert.Empty(Check(Device(Headset, null)));
    }

    [Fact]
    public void IgnoresDisconnectedDevices()
    {
        // Windows keeps the last level of a disconnected device; it is not draining any more
        Assert.Empty(Check(Device(Headset, 10, isConnected: false)));
        Assert.Equal([Headset], Check(Device(Headset, 10)));
    }

    [Fact]
    public void DoesNotWarnAgainAfterReconnectingUncharged()
    {
        Check(Device(Headset, 10));
        Check(Device(Headset, 10, isConnected: false));

        Assert.Empty(Check(Device(Headset, 10)));
    }

    [Fact]
    public void WarnsAtTheDevicesOwnThreshold()
    {
        _thresholds[Headset] = 40;

        Assert.Empty(Check(Device(Headset, 41), Device(Mouse, 41)));
        Assert.Equal([Headset], Check(Device(Headset, 40), Device(Mouse, 40)));
    }

    [Fact]
    public void RearmsTenPointsAboveTheDevicesOwnThreshold()
    {
        _thresholds[Headset] = 40;
        Check(Device(Headset, 35));

        Assert.Empty(Check(Device(Headset, 50)));
        Assert.Empty(Check(Device(Headset, 40)));
        Check(Device(Headset, 51));
        Assert.Equal([Headset], Check(Device(Headset, 40)));
    }

    [Fact]
    public void NeverWarnsWhenTheThresholdIsOff()
    {
        _thresholds[Headset] = 0;

        Assert.Empty(Check(Device(Headset, 5)));
        Assert.Empty(Check(Device(Headset, 0)));
    }

    [Fact]
    public void WarnsAtOnceWhenTheThresholdIsRaisedAboveTheLevel()
    {
        Assert.Empty(Check(Device(Headset, 35)));

        _thresholds[Headset] = 40;

        Assert.Equal([Headset], Check(Device(Headset, 35)));
    }

    [Fact]
    public void WarnsAtTheLoweredThresholdAfterAnEarlierWarning()
    {
        _thresholds[Headset] = 30;
        Check(Device(Headset, 28));

        _thresholds[Headset] = 15;
        _notifier.Reset(Headset);

        Assert.Empty(Check(Device(Headset, 28)));
        Assert.Equal([Headset], Check(Device(Headset, 15)));
    }

    private Guid[] Check(params BatteryDevice[] devices) =>
        _notifier.Check(devices, id => _thresholds.GetValueOrDefault(id, BatteryLevel.DefaultLowThreshold))
            .Select(device => device.Id).ToArray();

    private static BatteryDevice Device(Guid id, int? percent, bool isConnected = true) =>
        new(id, "Device", DeviceKind.Headset, percent, isConnected);
}
