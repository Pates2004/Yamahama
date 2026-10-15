using System.IO;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class StartupPreferencesTests
{
    public StartupPreferencesTests()
    {
        Localization.Initialize();
    }

    [Fact]
    public void StartupDefaultsConnectWithoutPoweringOnOrOff()
    {
        UiPreferences settings = new();
        Assert.False(settings.StartWithWindows);
        Assert.True(settings.AutoConnectAtStartup);
        Assert.False(settings.PowerOnAtStartup);
        Assert.False(settings.PowerOffOnExit);
        Assert.Null(settings.StartupDeviceAddress);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StartupOptionsRoundTripAndRemovingTheTargetDisablesBothOptions(bool autoConnect, bool powerOn)
    {
        string path = ConfigurationTests.TestPath($"startup-preferences-{autoConnect}-{powerOn}.json");
        UiPreferences settings = new UiPreferences().Remember(new SoundbarIdentity("SR-C30A", "1.00", 0x27, 0), new("Saved", 7, 0))
            with { StartWithWindows = true, AutoConnectAtStartup = autoConnect, PowerOnAtStartup = powerOn, StartupDeviceAddress = 7 };
        Assert.True(settings.TrySave(out string? saveWarning, path));
        Assert.Null(saveWarning);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.True(loaded.StartWithWindows);
        Assert.Equal(autoConnect, loaded.AutoConnectAtStartup);
        Assert.Equal(powerOn, loaded.PowerOnAtStartup);
        Assert.Equal(7UL, loaded.StartupDeviceAddress);
        UiPreferences removed = loaded.RemoveDevice(7);
        Assert.True(removed.StartWithWindows);
        Assert.False(removed.AutoConnectAtStartup);
        Assert.False(removed.PowerOnAtStartup);
        Assert.Null(removed.StartupDeviceAddress);
        Assert.Empty(removed.SavedDevices);
        Assert.Equal(autoConnect, loaded.AutoConnectAtStartup);
        Assert.Equal(powerOn, loaded.PowerOnAtStartup);
        Assert.Equal(7UL, Assert.Single(loaded.SavedDevices).Address);
        Assert.True(removed.TrySave(out _, path));
        UiPreferences reloaded = UiPreferences.Load(out warning, path);
        Assert.Null(warning);
        Assert.True(reloaded.StartWithWindows);
        Assert.False(reloaded.AutoConnectAtStartup);
        Assert.False(reloaded.PowerOnAtStartup);
        Assert.Null(reloaded.StartupDeviceAddress);
        Assert.Empty(reloaded.SavedDevices);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RemovingAnotherOrUnknownDevicePreservesStartupOptions(bool autoConnect, bool powerOn)
    {
        string path = ConfigurationTests.TestPath($"startup-other-removal-{autoConnect}-{powerOn}.json");
        UiPreferences settings = new()
        {
            SavedDevices = [new("Target", 7), new("Other", 8)],
            StartupDeviceAddress = 7,
            AutoConnectAtStartup = autoConnect,
            PowerOnAtStartup = powerOn
        };
        UiPreferences removed = settings.RemoveDevice(8).RemoveDevice(99);
        Assert.Equal(7UL, Assert.Single(removed.SavedDevices).Address);
        Assert.Equal(7UL, removed.StartupDeviceAddress);
        Assert.Equal(autoConnect, removed.AutoConnectAtStartup);
        Assert.Equal(powerOn, removed.PowerOnAtStartup);
        Assert.Equal(2, settings.SavedDevices.Length);
        Assert.True(removed.TrySave(out string? warning, path));
        Assert.Null(warning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingAutoConnectChoiceUsesNewDefaultWithoutChangingExplicitPowerOn(bool powerOn)
    {
        string path = ConfigurationTests.TestPath($"startup-legacy-{powerOn}.json");
        string json = "{\"SchemaVersion\":1,\"FirstRunCompleted\":true,\"Language\":\"en\",\"Theme\":\"Dark\","
            + "\"StartWithWindows\":true,\"PowerOnAtStartup\":" + (powerOn ? "true" : "false")
            + ",\"StartupDeviceAddress\":7,\"SavedDevices\":[{\"Name\":\"Saved\",\"Address\":7}]}";
        File.WriteAllText(path, json);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.True(loaded.IsSetupComplete);
        Assert.Equal("en", loaded.Language);
        Assert.Equal("Dark", loaded.Theme);
        Assert.True(loaded.StartWithWindows);
        Assert.True(loaded.AutoConnectAtStartup);
        Assert.Equal(powerOn, loaded.PowerOnAtStartup);
        Assert.False(loaded.PowerOffOnExit);
        Assert.Equal(7UL, loaded.StartupDeviceAddress);
        Assert.Equal(7UL, Assert.Single(loaded.SavedDevices).Address);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("{\"PowerOnAtStartup\":true}")]
    [InlineData("{\"AutoConnectAtStartup\":true,\"PowerOnAtStartup\":true}")]
    [InlineData("{\"StartupDeviceAddress\":7}")]
    [InlineData("{\"AutoConnectAtStartup\":true,\"StartupDeviceAddress\":7}")]
    [InlineData("{\"PowerOnAtStartup\":true,\"StartupDeviceAddress\":7}")]
    [InlineData("{\"AutoConnectAtStartup\":true,\"PowerOnAtStartup\":true,\"StartupDeviceAddress\":7}")]
    [InlineData("{\"AutoConnectAtStartup\":true,\"StartupDeviceAddress\":8,\"SavedDevices\":[{\"Name\":\"Saved\",\"Address\":7}]}")]
    [InlineData("{\"PowerOnAtStartup\":true,\"StartupDeviceAddress\":8,\"SavedDevices\":[{\"Name\":\"Saved\",\"Address\":7}]}")]
    public void UnknownStartupTargetsAreRejectedWithoutOverwriting(string json)
    {
        string path = ConfigurationTests.TestPath("invalid-startup.json");
        File.WriteAllText(path, json);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.True(loaded.AutoConnectAtStartup);
        Assert.False(loaded.PowerOnAtStartup);
        Assert.Null(loaded.StartupDeviceAddress);
        Assert.NotNull(warning);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("\"true\"")]
    [InlineData("\"false\"")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void MalformedAutoConnectValuesAreRejectedWithoutOverwriting(string value)
    {
        string path = ConfigurationTests.TestPath("startup-invalid-auto-connect.json");
        string json = "{\"AutoConnectAtStartup\":" + value
            + ",\"StartupDeviceAddress\":7,\"SavedDevices\":[{\"Name\":\"Saved\",\"Address\":7}]}";
        File.WriteAllText(path, json);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.NotNull(warning);
        Assert.True(loaded.AutoConnectAtStartup);
        Assert.False(loaded.PowerOnAtStartup);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SavingRejectsUnknownTargetsAndPowerOnWithoutATarget(bool autoConnect, bool powerOn)
    {
        string path = ConfigurationTests.TestPath($"startup-invalid-save-{autoConnect}-{powerOn}.json");
        UiPreferences original = new() { SavedDevices = [new("Saved", 7)], StartupDeviceAddress = 7 };
        Assert.True(original.TrySave(out _, path));
        byte[] originalBytes = File.ReadAllBytes(path);
        foreach (ulong? address in powerOn ? new ulong?[] { null, 8 } : new ulong?[] { 8 })
        {
            UiPreferences invalid = original with
            {
                AutoConnectAtStartup = autoConnect,
                PowerOnAtStartup = powerOn,
                StartupDeviceAddress = address
            };
            Assert.False(invalid.TrySave(out string? warning, path));
            Assert.NotNull(warning);
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"AutoConnectAtStartup\":true}")]
    [InlineData("{\"SavedDevices\":[{\"Name\":\"Only\",\"Address\":7}]}")]
    [InlineData("{\"AutoConnectAtStartup\":true,\"SavedDevices\":[{\"Name\":\"First\",\"Address\":7},{\"Name\":\"Second\",\"Address\":8}]}")]
    public void ConnectOnlyWithoutATargetIsValidAndDoesNotChooseFromSavedDevices(string json)
    {
        string path = ConfigurationTests.TestPath("startup-no-target.json");
        File.WriteAllText(path, json);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.True(loaded.AutoConnectAtStartup);
        Assert.False(loaded.PowerOnAtStartup);
        Assert.False(loaded.PowerOffOnExit);
        Assert.Null(loaded.StartupDeviceAddress);
        Assert.Equal(json, File.ReadAllText(path));
        Assert.True(loaded.TrySave(out warning, path));
        Assert.Null(warning);
        UiPreferences saved = UiPreferences.Load(out warning, path);
        Assert.Null(warning);
        Assert.True(saved.AutoConnectAtStartup);
        Assert.Null(saved.StartupDeviceAddress);
        Assert.Equal(loaded.SavedDevices, saved.SavedDevices);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void ExplicitStartupAndExitPowerChoicesSurviveIndependently(bool autoConnect, bool powerOn, bool powerOff)
    {
        string path = ConfigurationTests.TestPath($"startup-exit-choices-{autoConnect}-{powerOn}-{powerOff}.json");
        UiPreferences original = new()
        {
            AutoConnectAtStartup = autoConnect, PowerOnAtStartup = powerOn, PowerOffOnExit = powerOff,
            SavedDevices = [new("Saved", 7)], StartupDeviceAddress = 7,
            VolumeControlMode = LevelControlMode.Buttons, SubwooferControlMode = LevelControlMode.Buttons
        };
        Assert.True(original.TrySave(out _, path));
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Equal(autoConnect, loaded.AutoConnectAtStartup);
        Assert.Equal(powerOn, loaded.PowerOnAtStartup);
        Assert.Equal(powerOff, loaded.PowerOffOnExit);
        Assert.False(loaded.KeepPoweredOn);
        Assert.False(loaded.StartWithWindows);
        Assert.Equal(LevelControlMode.Buttons, loaded.VolumeControlMode);
        Assert.Equal(LevelControlMode.Buttons, loaded.SubwooferControlMode);
        Assert.Equal(powerOff, loaded.RemoveDevice(7).PowerOffOnExit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExitPowerPreferenceRequiresNeitherAStartupTargetNorProtection(bool enabled)
    {
        string path = ConfigurationTests.TestPath($"exit-no-target-{enabled}.json");
        Assert.True(new UiPreferences { PowerOffOnExit = enabled }.TrySave(out _, path));
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Equal(enabled, loaded.PowerOffOnExit);
        Assert.True(loaded.AutoConnectAtStartup);
        Assert.False(loaded.PowerOnAtStartup);
        Assert.False(loaded.KeepPoweredOn);
        Assert.Null(loaded.StartupDeviceAddress);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("\"true\"")]
    [InlineData("\"false\"")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void MalformedExitPowerValuesAreRejectedWithoutOverwriting(string value)
    {
        string path = ConfigurationTests.TestPath("exit-invalid-boolean.json");
        string json = "{\"PowerOffOnExit\":" + value + "}";
        File.WriteAllText(path, json);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.NotNull(warning);
        Assert.False(loaded.PowerOffOnExit);
        Assert.Null(loaded.StartupDeviceAddress);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void FirstNewVerifiedDeviceBecomesTheTargetWithoutEnablingPowerCommands()
    {
        SoundbarIdentity identity = new("SR-C30A", "1.00", 0x27, 0);
        UiPreferences original = new();
        UiPreferences first = original.Remember(identity, new("First", 7, 0));
        UiPreferences second = first.Remember(identity, new("Second", 8, 0));
        Assert.Null(original.StartupDeviceAddress);
        Assert.Equal(7UL, first.StartupDeviceAddress);
        Assert.Equal(7UL, second.StartupDeviceAddress);
        Assert.True(second.AutoConnectAtStartup);
        Assert.False(second.PowerOnAtStartup);
        Assert.False(second.PowerOffOnExit);
        UiPreferences disabled = (original with { AutoConnectAtStartup = false }).Remember(identity, new("First", 7, 0));
        Assert.Equal(7UL, disabled.StartupDeviceAddress);
        Assert.False(disabled.AutoConnectAtStartup);
    }

    [Fact]
    public void RememberingExistingDevicesDoesNotInventATargetButANewVerifiedDeviceCanBeSelected()
    {
        SoundbarIdentity identity = new("SR-C30A", "1.00", 0x27, 0);
        UiPreferences original = new() { SavedDevices = [new("First", 7), new("Second", 8)] };
        Assert.Null(original.Remember(identity, new("Renamed first", 7, 0)).StartupDeviceAddress);
        Assert.Null(original.Remember(identity, new("Renamed second", 8, 0)).StartupDeviceAddress);
        UiPreferences remembered = original.Remember(identity, new("Newly verified", 9, 0));
        Assert.Equal(9UL, remembered.StartupDeviceAddress);
        Assert.Equal(3, remembered.SavedDevices.Length);
        Assert.Null(original.StartupDeviceAddress);
        Assert.Throws<ArgumentException>(() => original.Remember(identity with { ModelCode = 0x28 }, new("Other model", 9, 0)));
    }

    [Fact]
    public void FailedSavePreservesExitPowerChoice()
    {
        string path = ConfigurationTests.TestPath("exit-failed-save.json");
        UiPreferences original = new() { PowerOffOnExit = true };
        Assert.True(original.TrySave(out _, path));
        byte[] originalBytes = File.ReadAllBytes(path);
        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False((original with { PowerOffOnExit = false }).TrySave(out string? warning, path));
            Assert.NotNull(warning);
        }
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.True(UiPreferences.Load(out _, path).PowerOffOnExit);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSavePreservesStartupOptionsAndTarget(bool removeTarget)
    {
        string path = ConfigurationTests.TestPath($"startup-failed-save-{removeTarget}.json");
        UiPreferences original = new()
        {
            SavedDevices = [new("Saved", 7)],
            StartWithWindows = true,
            AutoConnectAtStartup = true,
            PowerOnAtStartup = true,
            StartupDeviceAddress = 7
        };
        Assert.True(original.TrySave(out _, path));
        byte[] originalBytes = File.ReadAllBytes(path);
        UiPreferences candidate = removeTarget
            ? original.RemoveDevice(7)
            : original with { AutoConnectAtStartup = false, PowerOnAtStartup = false };
        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(candidate.TrySave(out string? warning, path));
            Assert.NotNull(warning);
        }
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        UiPreferences loaded = UiPreferences.Load(out string? loadWarning, path);
        Assert.Null(loadWarning);
        Assert.True(loaded.StartWithWindows);
        Assert.True(loaded.AutoConnectAtStartup);
        Assert.True(loaded.PowerOnAtStartup);
        Assert.Equal(7UL, loaded.StartupDeviceAddress);
        Assert.Equal(7UL, Assert.Single(loaded.SavedDevices).Address);
        Assert.True(original.AutoConnectAtStartup);
        Assert.True(original.PowerOnAtStartup);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
    }
}
