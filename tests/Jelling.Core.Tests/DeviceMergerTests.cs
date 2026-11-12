namespace Jelling.Core.Tests;

// Instance IDs, container IDs and category IDs are copied from a real probe of a Windows 11 machine
public class DeviceMergerTests
{
    private static readonly Guid Epos = Guid.Parse("00000000-0000-5000-8000-000000000001");
    private static readonly Guid Mouse = Guid.Parse("00000000-0000-5000-8000-000000000002");
    private static readonly Guid EchoShow = Guid.Parse("00000000-0000-5000-8000-000000000003");
    private static readonly Guid MxKeysOld = Guid.Parse("00000000-0000-5000-8000-000000000004");

    [Fact]
    public void TakesClassicBatteryFromServiceNodeOfSameContainer()
    {
        PnpNode[] nodes =
        [
            new(@"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}_VID&00010082_PID&0185\7&1a2b3c4d&0&001A7D000001_C00000000", Epos, 100),
            new(@"BTHENUM\Dev_001A7D000001\7&1a2b3c4d&0&BluetoothDevice_001A7D000001", Epos, null),
        ];
        DeviceContainer[] containers = [new(Epos, "EPOS ADAPT 260", ["Communication.Headset.Bluetooth", "Audio"], IsConnected: true)];

        var devices = DeviceMerger.Merge(nodes, containers);

        Assert.Equal([new BatteryDevice(Epos, "EPOS ADAPT 260", DeviceKind.Headset, 100, IsConnected: true)], devices);
    }

    [Fact]
    public void ListsDeviceWithoutBatteryValue()
    {
        PnpNode[] nodes = [new(@"BTHENUM\Dev_001A7D000002\7&1a2b3c4d&0&BluetoothDevice_001A7D000002", EchoShow, null)];
        DeviceContainer[] containers = [new(EchoShow, "Echo Show 8-4JQ", ["Audio.Speakers"], IsConnected: true)];

        var devices = DeviceMerger.Merge(nodes, containers);

        Assert.Equal([new BatteryDevice(EchoShow, "Echo Show 8-4JQ", DeviceKind.Speaker, null, IsConnected: true)], devices);
    }

    [Fact]
    public void IgnoresNodesThatAreNotBluetoothDevices()
    {
        var dongle = Guid.Parse("00000000-0000-1000-8000-000000000005");
        PnpNode[] nodes = [new(@"USB\VID_1395&PID_0291\A002230211700233", dongle, null)];
        DeviceContainer[] containers = [new(dongle, "EPOS ADAPT 260", ["Audio", "Component.Hub.USB", "Input"], IsConnected: true)];

        Assert.Empty(DeviceMerger.Merge(nodes, containers));
    }

    [Fact]
    public void IgnoresContainerWithoutBluetoothDeviceNode()
    {
        // Only a service node, e.g. while Windows is still enumerating the device
        PnpNode[] nodes = [new(@"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}_LOCALMFG&0000\7&1a2b3c4d&0&001A7D000001_C00000000", Epos, 80)];
        DeviceContainer[] containers = [new(Epos, "EPOS ADAPT 260", ["Communication.Headset.Bluetooth"], IsConnected: true)];

        Assert.Empty(DeviceMerger.Merge(nodes, containers));
    }

    [Fact]
    public void TakesBatteryFromServiceNodeListedAfterDeviceNodeWithout()
    {
        PnpNode[] nodes =
        [
            new(@"BTHENUM\Dev_001A7D000001\7&1a2b3c4d&0&BluetoothDevice_001A7D000001", Epos, null),
            new(@"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}_VID&00010082_PID&0185\7&1a2b3c4d&0&001A7D000001_C00000000", Epos, 70),
        ];
        DeviceContainer[] containers = [new(Epos, "EPOS ADAPT 260", ["Communication.Headset.Bluetooth"], IsConnected: true)];

        Assert.Equal(70, DeviceMerger.Merge(nodes, containers).Single().BatteryPercent);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(255)]
    [InlineData(-1)]
    public void IgnoresBatteryValuesOutsidePercentRange(int invalid)
    {
        PnpNode[] nodes =
        [
            new(@"BTHENUM\Dev_001A7D000001\7&1a2b3c4d&0&BluetoothDevice_001A7D000001", Epos, invalid),
            new(@"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}_VID&00010082_PID&0185\7&1a2b3c4d&0&001A7D000001_C00000000", Epos, 60),
        ];
        PnpNode[] onlyInvalid = [nodes[0]];
        DeviceContainer[] containers = [new(Epos, "EPOS ADAPT 260", ["Communication.Headset.Bluetooth"], IsConnected: true)];

        Assert.Equal(60, DeviceMerger.Merge(nodes, containers).Single().BatteryPercent);
        Assert.Null(DeviceMerger.Merge(onlyInvalid, containers).Single().BatteryPercent);
    }

    [Fact]
    public void IgnoresBluetoothNodeWithoutContainer()
    {
        PnpNode[] nodes = [new(@"BTHLE\Dev_c0ffee000002\7&5e6f708&0&c0ffee000002", Mouse, 100)];

        Assert.Empty(DeviceMerger.Merge(nodes, []));
    }

    [Fact]
    public void SortsConnectedDevicesFirstThenByName()
    {
        PnpNode[] nodes =
        [
            new(@"BTHLE\Dev_c0ffee000001\7&5e6f708&0&c0ffee000001", MxKeysOld, 20),
            new(@"BTHLE\Dev_c0ffee000002\7&5e6f708&0&c0ffee000002", Mouse, 100),
            new(@"BTHENUM\Dev_001A7D000002\7&1a2b3c4d&0&BluetoothDevice_001A7D000002", EchoShow, null),
        ];
        DeviceContainer[] containers =
        [
            new(MxKeysOld, "MX Keys", ["Input.Keyboard", "Input.Mouse"], IsConnected: false),
            new(Mouse, "M585/M590", ["Input.Mouse", "Input.Keyboard"], IsConnected: true),
            new(EchoShow, "Echo Show 8-4JQ", ["Audio.Speakers"], IsConnected: true),
        ];

        var names = DeviceMerger.Merge(nodes, containers).Select(d => d.Name);

        Assert.Equal(["Echo Show 8-4JQ", "M585/M590", "MX Keys"], names);
    }

    [Theory]
    [InlineData(DeviceKind.Mouse, "Input.Mouse", "Input.Keyboard")]
    [InlineData(DeviceKind.Keyboard, "Input.Keyboard", "Input.Mouse")]
    [InlineData(DeviceKind.Headset, "Communication.Headset.Bluetooth", "Audio")]
    [InlineData(DeviceKind.Headset, "Audio.Headphone")]
    [InlineData(DeviceKind.Speaker, "Audio.Speakers")]
    [InlineData(DeviceKind.GameController, "Input.Gaming")]
    [InlineData(DeviceKind.Mouse, "Audio", "Input.Mouse")]
    [InlineData(DeviceKind.Other, "Audio", "Component.Hub.USB", "Input")]
    [InlineData(DeviceKind.Other)]
    public void MapsFirstKnownCategoryToKind(DeviceKind expected, params string[] categoryIds)
    {
        Assert.Equal(expected, DeviceMerger.KindFromCategories(categoryIds));
    }

    [Fact]
    public void PicksTheSameLevelWhateverOrderTheNodesComeIn()
    {
        PnpNode service = new(@"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}_VID&00010082_PID&0185\7&1a2b3c4d&0&001A7D000001_C00000000", Epos, 80);
        PnpNode device = new(@"BTHENUM\Dev_001A7D000001\7&1a2b3c4d&0&BluetoothDevice_001A7D000001", Epos, 40);
        DeviceContainer[] containers = [new(Epos, "EPOS ADAPT 260", ["Communication.Headset.Bluetooth"], IsConnected: true)];

        var forward = DeviceMerger.Merge([service, device], containers);
        var backward = DeviceMerger.Merge([device, service], containers);

        Assert.Equal(forward, backward);
    }

    [Theory]
    // A2DP and Hands-Free, the two audio filters a classic headset brings
    [InlineData(@"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&00010082_PID&0185\7&1a2b3c4d&0&001A7D000001_C00000000")]
    [InlineData(@"BTHHFENUM\BthHFPAudio\8&2b104fd4&0&97")]
    public void MarksDeviceConnectableWhenItHasBluetoothAudioFilter(string filterInstanceId)
    {
        PnpNode[] nodes = [new(@"BTHENUM\Dev_001A7D000001\7&1a2b3c4d&0&BluetoothDevice_001A7D000001", Epos, 60)];
        DeviceContainer[] containers = [new(Epos, "EPOS ADAPT 260", ["Communication.Headset.Bluetooth"], IsConnected: false)];

        var device = DeviceMerger.Merge(nodes, containers, [new AudioFilter(filterInstanceId, Epos)]).Single();

        Assert.True(device.CanConnect);
    }

    [Fact]
    public void LeavesDeviceWithoutBluetoothAudioFilterUnconnectable()
    {
        PnpNode[] nodes =
        [
            new(@"BTHENUM\Dev_001A7D000001\7&1a2b3c4d&0&BluetoothDevice_001A7D000001", Epos, 60),
            new(@"BTHLE\Dev_c1d2e3f4a5b6\8&1a2b3c4d&0&c1d2e3f4a5b6", Mouse, 90),
        ];
        DeviceContainer[] containers =
        [
            new(Epos, "EPOS ADAPT 260", ["Communication.Headset.Bluetooth"], IsConnected: true),
            new(Mouse, "M585/M590", ["Input.Mouse"], IsConnected: true),
        ];
        // An audio filter in the headset's container that Bluetooth does not drive, such as a USB dongle's
        AudioFilter[] filters = [new(@"USB\VID_1395&PID_0300&MI_00\8&2c1d3e4f&0&0000", Epos)];

        var devices = DeviceMerger.Merge(nodes, containers, filters);

        Assert.All(devices, device => Assert.False(device.CanConnect));
    }
}
