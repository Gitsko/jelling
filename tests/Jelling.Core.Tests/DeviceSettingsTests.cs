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
        Assert.Equal(LevelDisplay.Number, settings.LevelDisplay);
    }

    [Fact]
    public void ShowsTheNumberByDefault()
    {
        Assert.Equal(LevelDisplay.Number, new DeviceSettings(SettingsPath).LevelDisplay);
    }

    [Fact]
    public void LevelDisplaySurvivesReload()
    {
        var settings = new DeviceSettings(SettingsPath);
        settings.SetLevelDisplay(LevelDisplay.Symbol);
        settings.TogglePin(Headset);

        var reloaded = new DeviceSettings(SettingsPath);

        Assert.Equal(LevelDisplay.Symbol, reloaded.LevelDisplay);
        Assert.True(reloaded.IsPinned(Headset));
    }

    [Theory]
    [InlineData("""{"ShowPercent":false}""", LevelDisplay.Symbol)]
    [InlineData("""{"ShowPercent":true}""", LevelDisplay.Number)]
    [InlineData("""{"LevelDisplay":"Ring","ShowPercent":false}""", LevelDisplay.Ring)]
    [InlineData("""{"LevelDisplay":"ring"}""", LevelDisplay.Ring)]
    public void ReadsTheLevelDisplayOrTheFormerPercentSetting(string json, LevelDisplay expected)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, json);

        Assert.Equal(expected, new DeviceSettings(SettingsPath).LevelDisplay);
    }

    [Theory]
    [InlineData("Bogus")]
    [InlineData("7")]
    public void UnknownLevelDisplayReadsAsNumberAndKeepsTheRest(string stored)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, $$"""{"PinnedDevices":["{{Headset}}"],"LevelDisplay":"{{stored}}"}""");

        var settings = new DeviceSettings(SettingsPath);

        Assert.Equal(LevelDisplay.Number, settings.LevelDisplay);
        Assert.True(settings.IsPinned(Headset));
        Assert.False(File.Exists(Path.ChangeExtension(SettingsPath, ".invalid.json")));
    }

    [Fact]
    public void WidgetHasItsNormalSizeByDefault()
    {
        Assert.Equal(1, new DeviceSettings(SettingsPath).WidgetScale);
    }

    [Fact]
    public void WidgetScaleSurvivesReload()
    {
        new DeviceSettings(SettingsPath).SetWidgetScale(3);

        Assert.Equal(3, new DeviceSettings(SettingsPath).WidgetScale);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void RejectsWidgetScalesThatAreNotOffered(int scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeviceSettings(SettingsPath).SetWidgetScale(scale));
    }

    [Fact]
    public void IgnoresAStoredWidgetScaleThatIsNotOffered()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, """{"WidgetScale":7}""");

        Assert.Equal(1, new DeviceSettings(SettingsPath).WidgetScale);
    }

    [Fact]
    public void WidgetIsOffUnlockedWithoutPositionByDefault()
    {
        var settings = new DeviceSettings(SettingsPath);

        Assert.False(settings.ShowWidget);
        Assert.False(settings.WidgetLocked);
        Assert.Null(settings.WidgetPosition);
    }

    [Fact]
    public void WidgetSettingsSurviveReload()
    {
        var settings = new DeviceSettings(SettingsPath);
        settings.SetShowWidget(true);
        settings.SetWidgetLocked(true);
        settings.SetWidgetPosition(new PixelPoint(-1500, 600));

        var reloaded = new DeviceSettings(SettingsPath);

        Assert.True(reloaded.ShowWidget);
        Assert.True(reloaded.WidgetLocked);
        Assert.Equal(new PixelPoint(-1500, 600), reloaded.WidgetPosition);
    }

    [Fact]
    public void FileFromBeforeTheWidgetReadsAsWidgetOff()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, $$"""{"PinnedDevices":["{{Headset}}"],"ShowPercent":false}""");

        var settings = new DeviceSettings(SettingsPath);

        Assert.False(settings.ShowWidget);
        Assert.False(settings.WidgetLocked);
        Assert.Null(settings.WidgetPosition);
        Assert.Equal(LevelDisplay.Symbol, settings.LevelDisplay);
    }

    [Fact]
    public void MissingFileMeansNothingOnTheWidget()
    {
        Assert.False(new DeviceSettings(SettingsPath).IsOnWidget(Headset));
    }

    [Fact]
    public void WidgetToggleSurvivesReloadWithoutPinning()
    {
        new DeviceSettings(SettingsPath).ToggleWidget(Headset);

        var reloaded = new DeviceSettings(SettingsPath);

        Assert.True(reloaded.IsOnWidget(Headset));
        Assert.False(reloaded.IsPinned(Headset));
        Assert.False(reloaded.IsOnWidget(Mouse));
    }

    [Fact]
    public void SecondWidgetToggleTakesTheDeviceOff()
    {
        var settings = new DeviceSettings(SettingsPath);
        settings.ToggleWidget(Headset);
        settings.ToggleWidget(Headset);

        Assert.False(new DeviceSettings(SettingsPath).IsOnWidget(Headset));
    }

    [Fact]
    public void PinningLeavesTheWidgetAlone()
    {
        new DeviceSettings(SettingsPath).TogglePin(Headset);

        var reloaded = new DeviceSettings(SettingsPath);

        Assert.True(reloaded.IsPinned(Headset));
        Assert.False(reloaded.IsOnWidget(Headset));
    }

    [Fact]
    public void FileFromBeforeTheSplitShowsThePinnedDevicesOnTheWidget()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, $$"""{"PinnedDevices":["{{Headset}}"],"ShowWidget":true}""");

        var settings = new DeviceSettings(SettingsPath);

        Assert.True(settings.IsOnWidget(Headset));
        Assert.True(settings.IsPinned(Headset));
        Assert.False(settings.IsOnWidget(Mouse));
    }

    [Fact]
    public void EmptyWidgetListIsNotFilledFromThePins()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, $$"""{"PinnedDevices":["{{Headset}}"],"WidgetDevices":[]}""");

        Assert.False(new DeviceSettings(SettingsPath).IsOnWidget(Headset));
    }

    [Fact]
    public void FileLockedAtStartIsReadBeforeTheFirstWidgetToggle()
    {
        new DeviceSettings(SettingsPath).ToggleWidget(Headset);
        DeviceSettings settings;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings = new DeviceSettings(SettingsPath);
        }

        settings.ToggleWidget(Mouse);

        var reloaded = new DeviceSettings(SettingsPath);
        Assert.True(reloaded.IsOnWidget(Headset));
        Assert.True(reloaded.IsOnWidget(Mouse));
    }

    [Fact]
    public void WidgetToggleWhileLockedKeepsWhatTheUserSawAfterTheReload()
    {
        new DeviceSettings(SettingsPath).ToggleWidget(Headset);
        DeviceSettings settings;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Read as defaults, so Headset looks off the widget and the toggle puts it on
            settings = new DeviceSettings(SettingsPath);
            Assert.Throws<IOException>(() => settings.ToggleWidget(Headset));
        }

        settings.SetLevelDisplay(LevelDisplay.Symbol);

        Assert.True(new DeviceSettings(SettingsPath).IsOnWidget(Headset));
    }

    [Fact]
    public void WidgetLevelDisplaySurvivesReloadWithoutChangingTheTaskbar()
    {
        new DeviceSettings(SettingsPath).SetWidgetLevelDisplay(LevelDisplay.Ring);

        var reloaded = new DeviceSettings(SettingsPath);

        Assert.Equal(LevelDisplay.Ring, reloaded.WidgetLevelDisplay);
        Assert.Equal(LevelDisplay.Number, reloaded.LevelDisplay);
    }

    [Fact]
    public void TaskbarLevelDisplayLeavesTheWidgetAlone()
    {
        var settings = new DeviceSettings(SettingsPath);
        settings.SetWidgetLevelDisplay(LevelDisplay.Ring);
        settings.SetLevelDisplay(LevelDisplay.Symbol);

        var reloaded = new DeviceSettings(SettingsPath);

        Assert.Equal(LevelDisplay.Ring, reloaded.WidgetLevelDisplay);
        Assert.Equal(LevelDisplay.Symbol, reloaded.LevelDisplay);
    }

    [Theory]
    [InlineData("""{"LevelDisplay":"Ring"}""", LevelDisplay.Ring)]
    [InlineData("""{"ShowPercent":false}""", LevelDisplay.Symbol)]
    [InlineData("""{"LevelDisplay":"Ring","WidgetLevelDisplay":"Bogus"}""", LevelDisplay.Ring)]
    [InlineData("""{"LevelDisplay":"Ring","WidgetLevelDisplay":"Symbol"}""", LevelDisplay.Symbol)]
    public void WidgetTakesTheTaskbarLevelDisplayUntilItHasOneOfItsOwn(string json, LevelDisplay expected)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, json);

        Assert.Equal(expected, new DeviceSettings(SettingsPath).WidgetLevelDisplay);
    }

    [Fact]
    public void FileLockedAtStartIsReadBeforeChangingTheWidgetLevelDisplay()
    {
        new DeviceSettings(SettingsPath).SetLevelDisplay(LevelDisplay.Symbol);
        DeviceSettings settings;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings = new DeviceSettings(SettingsPath);
        }

        settings.SetWidgetLevelDisplay(LevelDisplay.Ring);

        var reloaded = new DeviceSettings(SettingsPath);
        Assert.Equal(LevelDisplay.Symbol, reloaded.LevelDisplay);
        Assert.Equal(LevelDisplay.Ring, reloaded.WidgetLevelDisplay);
    }

    [Fact]
    public void WidgetShowsNoNamesByDefault()
    {
        Assert.False(new DeviceSettings(SettingsPath).WidgetShowNames);
    }

    [Fact]
    public void WidgetNamesSurviveReload()
    {
        new DeviceSettings(SettingsPath).SetWidgetShowNames(true);

        Assert.True(new DeviceSettings(SettingsPath).WidgetShowNames);
    }

    [Fact]
    public void FileLockedAtStartIsReadBeforeTurningOnTheWidgetNames()
    {
        new DeviceSettings(SettingsPath).ToggleWidget(Headset);
        DeviceSettings settings;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings = new DeviceSettings(SettingsPath);
        }

        settings.SetWidgetShowNames(true);

        var reloaded = new DeviceSettings(SettingsPath);
        Assert.True(reloaded.IsOnWidget(Headset));
        Assert.True(reloaded.WidgetShowNames);
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
    public void FileLockedAtStartIsReadBeforeTheFirstSave()
    {
        new DeviceSettings(SettingsPath).TogglePin(Headset);
        DeviceSettings settings;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings = new DeviceSettings(SettingsPath);
        }

        settings.TogglePin(Mouse);

        var reloaded = new DeviceSettings(SettingsPath);
        Assert.True(reloaded.IsPinned(Headset));
        Assert.True(reloaded.IsPinned(Mouse));
    }

    [Fact]
    public void FileLockedAtStartIsReadBeforeSettingAThreshold()
    {
        new DeviceSettings(SettingsPath).TogglePin(Headset);
        DeviceSettings settings;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings = new DeviceSettings(SettingsPath);
        }

        settings.SetLowThreshold(Mouse, 40);

        var reloaded = new DeviceSettings(SettingsPath);
        Assert.True(reloaded.IsPinned(Headset));
        Assert.Equal(40, reloaded.LowThreshold(Mouse));
    }

    [Fact]
    public void FileLockedAtStartIsReadBeforeChangingTheLevelDisplay()
    {
        new DeviceSettings(SettingsPath).TogglePin(Headset);
        DeviceSettings settings;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings = new DeviceSettings(SettingsPath);
        }

        settings.SetLevelDisplay(LevelDisplay.Symbol);

        var reloaded = new DeviceSettings(SettingsPath);
        Assert.True(reloaded.IsPinned(Headset));
        Assert.Equal(LevelDisplay.Symbol, reloaded.LevelDisplay);
    }

    [Fact]
    public void ChangesWhileLockedSurviveTheReload()
    {
        new DeviceSettings(SettingsPath).TogglePin(Headset);
        DeviceSettings settings;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings = new DeviceSettings(SettingsPath);
            Assert.Throws<IOException>(() => settings.TogglePin(Mouse));
            Assert.Throws<IOException>(() => settings.SetLowThreshold(Mouse, 40));
        }

        settings.SetLevelDisplay(LevelDisplay.Symbol);

        Assert.True(settings.IsPinned(Mouse));
        var reloaded = new DeviceSettings(SettingsPath);
        Assert.True(reloaded.IsPinned(Headset));
        Assert.True(reloaded.IsPinned(Mouse));
        Assert.Equal(40, reloaded.LowThreshold(Mouse));
        Assert.Equal(LevelDisplay.Symbol, reloaded.LevelDisplay);
    }

    [Fact]
    public void ToggleWhileLockedKeepsWhatTheUserSawAfterTheReload()
    {
        new DeviceSettings(SettingsPath).TogglePin(Headset);
        DeviceSettings settings;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Read as defaults, so Headset looks unpinned and the toggle pins it
            settings = new DeviceSettings(SettingsPath);
            Assert.Throws<IOException>(() => settings.TogglePin(Headset));
        }

        settings.SetLevelDisplay(LevelDisplay.Symbol);

        Assert.True(new DeviceSettings(SettingsPath).IsPinned(Headset));
    }

    [Fact]
    public void PinAfterAFailedReadKeepsWhatTheUserChose()
    {
        new DeviceSettings(SettingsPath).SetPinned(Headset, true);
        DeviceSettings settings;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Read as defaults, so the row showed Headset unpinned and the user pins it
            settings = new DeviceSettings(SettingsPath);
        }

        settings.SetPinned(Headset, true);

        Assert.True(new DeviceSettings(SettingsPath).IsPinned(Headset));
    }

    [Fact]
    public void WidgetChoiceAfterAFailedReadKeepsWhatTheUserChose()
    {
        new DeviceSettings(SettingsPath).SetOnWidget(Headset, true);
        DeviceSettings settings;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings = new DeviceSettings(SettingsPath);
        }

        settings.SetOnWidget(Headset, true);

        Assert.True(new DeviceSettings(SettingsPath).IsOnWidget(Headset));
    }

    [Fact]
    public void WidgetChangesWhileLockedSurviveTheReload()
    {
        new DeviceSettings(SettingsPath).SetPinned(Headset, true);
        DeviceSettings settings;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings = new DeviceSettings(SettingsPath);
            Assert.Throws<IOException>(() => settings.SetWidgetShowNames(true));
            Assert.Throws<IOException>(() => settings.SetWidgetLevelDisplay(LevelDisplay.Ring));
        }

        settings.SetLevelDisplay(LevelDisplay.Symbol);

        var reloaded = new DeviceSettings(SettingsPath);
        Assert.True(reloaded.IsPinned(Headset));
        Assert.True(reloaded.WidgetShowNames);
        Assert.Equal(LevelDisplay.Ring, reloaded.WidgetLevelDisplay);
        Assert.Equal(LevelDisplay.Symbol, reloaded.LevelDisplay);
    }

    [Fact]
    public void FieldsFromANewerVersionSurviveASave()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, """{"PinnedDevices":[],"FutureField":{"Kept":1}}""");

        new DeviceSettings(SettingsPath).SetPinned(Headset, true);

        var saved = File.ReadAllText(SettingsPath);
        Assert.Contains("\"FutureField\":{\"Kept\":1}", saved);
        Assert.True(new DeviceSettings(SettingsPath).IsPinned(Headset));
    }

    [Fact]
    public void CorruptFileThatCannotBeKeptAsideReadsAsDefaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "{ not json");
        var keptAside = Path.Combine(_folder, "settings.invalid.json");
        File.WriteAllText(keptAside, "older");

        using (new FileStream(keptAside, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(new DeviceSettings(SettingsPath).IsPinned(Headset));
        }

        Assert.Equal("older", File.ReadAllText(keptAside));
    }

    [Fact]
    public void SaveLeavesNoFileBehind()
    {
        new DeviceSettings(SettingsPath).TogglePin(Headset);

        Assert.Equal([SettingsPath], Directory.GetFiles(_folder));
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
