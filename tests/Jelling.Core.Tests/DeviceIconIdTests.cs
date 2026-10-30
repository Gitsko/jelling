namespace Jelling.Core.Tests;

public class DeviceIconIdTests
{
    private const string ExePath = @"C:\Tools\Jelling\Jelling.App.exe";
    private static readonly Guid Headset = Guid.Parse("00000000-0000-5000-8000-000000000001");
    private static readonly Guid Mouse = Guid.Parse("00000000-0000-5000-8000-000000000002");

    [Fact]
    public void IsStableForSameDeviceAndPath()
    {
        Assert.Equal(DeviceIconId.For(ExePath, Headset), DeviceIconId.For(ExePath, Headset));
    }

    [Fact]
    public void IgnoresPathCase()
    {
        Assert.Equal(DeviceIconId.For(ExePath, Headset), DeviceIconId.For(ExePath.ToLowerInvariant(), Headset));
    }

    [Fact]
    public void DiffersPerDevice()
    {
        Assert.NotEqual(DeviceIconId.For(ExePath, Headset), DeviceIconId.For(ExePath, Mouse));
    }

    [Fact]
    public void DiffersPerExePath()
    {
        Assert.NotEqual(DeviceIconId.For(ExePath, Headset), DeviceIconId.For(@"D:\Other\Jelling.App.exe", Headset));
    }

    [Fact]
    public void DiffersFromDeviceId()
    {
        Assert.NotEqual(Headset, DeviceIconId.For(ExePath, Headset));
    }
}
