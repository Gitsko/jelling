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

    // Windows keeps an icon's taskbar place by its GUID; a changed derivation would lose every user's arrangement
    [Fact]
    public void DeviceIdStaysTheSameAcrossVersions()
    {
        Assert.Equal(Guid.Parse("8fc33e03-bf54-3391-6c5e-6dc7b6773e10"), DeviceIconId.For(ExePath, Headset));
    }

    [Fact]
    public void SeparatorIdStaysTheSameAcrossVersions()
    {
        Assert.Equal(Guid.Parse("44bd8643-56ef-2165-8182-770b5659f722"), DeviceIconId.Separator(ExePath));
    }

    [Fact]
    public void SeparatorIsStableForSamePath()
    {
        Assert.Equal(DeviceIconId.Separator(ExePath), DeviceIconId.Separator(ExePath.ToLowerInvariant()));
    }

    [Fact]
    public void SeparatorDiffersPerExePath()
    {
        Assert.NotEqual(DeviceIconId.Separator(ExePath), DeviceIconId.Separator(@"D:\Other\Jelling.App.exe"));
    }

    [Fact]
    public void SeparatorDiffersFromDeviceIcons()
    {
        Assert.NotEqual(DeviceIconId.Separator(ExePath), DeviceIconId.For(ExePath, Headset));
    }

    [Fact]
    public void DiffersFromDeviceId()
    {
        Assert.NotEqual(Headset, DeviceIconId.For(ExePath, Headset));
    }
}
