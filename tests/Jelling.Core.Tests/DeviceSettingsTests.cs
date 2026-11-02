namespace Jelling.Core.Tests;

public sealed class DeviceSettingsTests : IDisposable
{
    private static readonly Guid Headset = Guid.Parse("00000000-0000-5000-8000-000000000001");
    private static readonly Guid Mouse = Guid.Parse("00000000-0000-5000-8000-000000000002");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"bb-settings-{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void MissingFileMeansNothingPinned()
    {
        Assert.False(new DeviceSettings(SettingsPath).IsPinned(Headset));
    }

    [Fact]
    public void ToggleSurvivesReload()
    {
        new DeviceSettings(SettingsPath).TogglePin(Headset);

        var reloaded = new DeviceSettings(SettingsPath);

        Assert.True(reloaded.IsPinned(Headset));
        Assert.False(reloaded.IsPinned(Mouse));
    }

    [Fact]
    public void SecondToggleUnpins()
    {
        var settings = new DeviceSettings(SettingsPath);
        settings.TogglePin(Headset);
        settings.TogglePin(Headset);

        Assert.False(new DeviceSettings(SettingsPath).IsPinned(Headset));
    }

    [Fact]
    public void CorruptFileMeansNothingPinned()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "{ not json");

        Assert.False(new DeviceSettings(SettingsPath).IsPinned(Headset));
    }

    [Fact]
    public void CorruptFileIsKeptAsideBeforeTheNextSave()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "{ not json");

        new DeviceSettings(SettingsPath).TogglePin(Headset);

        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(_folder, "settings.invalid.json")));
        Assert.True(new DeviceSettings(SettingsPath).IsPinned(Headset));
    }

    [Fact]
    public void UnsetThresholdIsTheDefault()
    {
        Assert.Equal(BatteryLevel.DefaultLowThreshold, new DeviceSettings(SettingsPath).LowThreshold(Headset));
    }

    [Fact]
    public void ThresholdSurvivesReload()
    {
        var settings = new DeviceSettings(SettingsPath);
        settings.SetLowThreshold(Headset, 40);
        settings.TogglePin(Mouse);

        var reloaded = new DeviceSettings(SettingsPath);

        Assert.Equal(40, reloaded.LowThreshold(Headset));
        Assert.Equal(BatteryLevel.DefaultLowThreshold, reloaded.LowThreshold(Mouse));
        Assert.True(reloaded.IsPinned(Mouse));
    }

    [Fact]
    public void ThresholdOffSurvivesReload()
    {
        new DeviceSettings(SettingsPath).SetLowThreshold(Headset, 0);

        Assert.Equal(0, new DeviceSettings(SettingsPath).LowThreshold(Headset));
    }

    [Fact]
    public void RejectsThresholdsThatAreNotOffered()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeviceSettings(SettingsPath).SetLowThreshold(Headset, 33));
    }

    [Fact]
    public void ReadsFilesFromBeforeThresholds()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, $$"""{"PinnedDevices":["{{Headset}}"]}""");

        var settings = new DeviceSettings(SettingsPath);

        Assert.True(settings.IsPinned(Headset));
        Assert.Equal(BatteryLevel.DefaultLowThreshold, settings.LowThreshold(Headset));
        Assert.True(settings.ShowPercent);
    }

    [Fact]
    public void ShowsPercentByDefault()
    {
        Assert.True(new DeviceSettings(SettingsPath).ShowPercent);
    }

    [Fact]
    public void HiddenPercentSurvivesReload()
    {
        var settings = new DeviceSettings(SettingsPath);
        settings.SetShowPercent(false);
        settings.TogglePin(Headset);

        var reloaded = new DeviceSettings(SettingsPath);

        Assert.False(reloaded.ShowPercent);
        Assert.True(reloaded.IsPinned(Headset));
    }

    [Fact]
    public void IgnoresStoredThresholdsThatAreNotOffered()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, $$$"""{"LowThresholds":{"{{{Headset}}}":33,"{{{Mouse}}}":30}}""");

        var settings = new DeviceSettings(SettingsPath);

        Assert.Equal(BatteryLevel.DefaultLowThreshold, settings.LowThreshold(Headset));
        Assert.Equal(30, settings.LowThreshold(Mouse));
    }

    [Fact]
    public void LockedFileReadsAsDefaultsButIsNotOverwritten()
    {
        new DeviceSettings(SettingsPath).TogglePin(Headset);

        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var settings = new DeviceSettings(SettingsPath);

            Assert.False(settings.IsPinned(Headset));
            Assert.Throws<IOException>(() => settings.TogglePin(Mouse));
        }

        var reloaded = new DeviceSettings(SettingsPath);
        Assert.True(reloaded.IsPinned(Headset));
        Assert.False(reloaded.IsPinned(Mouse));
    }

    [Fact]
    public void NullLiteralMeansDefaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "null");

        var settings = new DeviceSettings(SettingsPath);
        settings.TogglePin(Headset);

        Assert.True(new DeviceSettings(SettingsPath).IsPinned(Headset));
    }

    [Fact]
    public void KeyThatIsNoGuidCountsAsCorruptFile()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, """{"LowThresholds":{"not-a-guid":30}}""");

        var settings = new DeviceSettings(SettingsPath);

        Assert.Equal(BatteryLevel.DefaultLowThreshold, settings.LowThreshold(Headset));
        Assert.True(File.Exists(Path.Combine(_folder, "settings.invalid.json")));
    }
}
