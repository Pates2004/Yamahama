using System.IO;
using System.Text.Json;
using Xunit;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class LevelControlPreferencesTests
{
    public LevelControlPreferencesTests()
    {
        Localization.Initialize();
    }

    [Fact]
    public void LevelControlsUseSlidersByDefault()
    {
        UiPreferences settings = new();
        Assert.Equal(LevelControlMode.Slider, settings.VolumeControlMode);
        Assert.Equal(LevelControlMode.Slider, settings.SubwooferControlMode);
    }

    [Theory]
    [InlineData("Buttons", "Buttons")]
    [InlineData("Buttons", "Slider")]
    [InlineData("Slider", "Buttons")]
    [InlineData("Slider", "Slider")]
    public void IndependentLevelControlModesRoundTripAsStrings(string volumeMode, string subwooferMode)
    {
        string path = ConfigurationTests.TestPath($"level-controls-{volumeMode}-{subwooferMode}.json");
        UiPreferences original = new()
        {
            VolumeControlMode = Enum.Parse<LevelControlMode>(volumeMode),
            SubwooferControlMode = Enum.Parse<LevelControlMode>(subwooferMode),
            UseReadOnlyValueFields = true,
            ShowHints = false,
            AutoApplySelections = false
        };
        Assert.True(original.TrySave(out string? saveWarning, path));
        Assert.Null(saveWarning);
        UiPreferences loaded = UiPreferences.Load(out string? loadWarning, path);
        Assert.Null(loadWarning);
        Assert.Equal(original.VolumeControlMode, loaded.VolumeControlMode);
        Assert.Equal(original.SubwooferControlMode, loaded.SubwooferControlMode);
        Assert.True(loaded.UseReadOnlyValueFields);
        Assert.False(loaded.ShowHints);
        Assert.False(loaded.AutoApplySelections);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("VolumeControlMode").ValueKind);
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("SubwooferControlMode").ValueKind);
        Assert.Equal(volumeMode, document.RootElement.GetProperty("VolumeControlMode").GetString());
        Assert.Equal(subwooferMode, document.RootElement.GetProperty("SubwooferControlMode").GetString());
    }

    [Theory]
    [InlineData("{}", "Slider", "Slider")]
    [InlineData("{\"VolumeControlMode\":\"Buttons\"}", "Buttons", "Slider")]
    [InlineData("{\"SubwooferControlMode\":\"Buttons\"}", "Slider", "Buttons")]
    [InlineData("{\"VolumeControlMode\":\"Slider\"}", "Slider", "Slider")]
    [InlineData("{\"SubwooferControlMode\":\"Slider\"}", "Slider", "Slider")]
    public void MissingLevelControlChoicesUseIndependentDefaults(string json, string volumeMode, string subwooferMode)
    {
        string path = ConfigurationTests.TestPath($"level-controls-missing-{volumeMode}-{subwooferMode}.json");
        File.WriteAllText(path, json);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Equal(Enum.Parse<LevelControlMode>(volumeMode), loaded.VolumeControlMode);
        Assert.Equal(Enum.Parse<LevelControlMode>(subwooferMode), loaded.SubwooferControlMode);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void OlderConfigurationPreservesExistingPreferencesAndSavedDevices()
    {
        string path = ConfigurationTests.TestPath("level-controls-legacy.json");
        string json = "{\"SchemaVersion\":1,\"FirstRunCompleted\":true,\"Language\":\"en\",\"Theme\":\"Dark\","
            + "\"UseReadOnlyValueFields\":true,\"ShowHints\":false,\"AutoApplySelections\":false,"
            + "\"PowerOnAtStartup\":true,\"StartupDeviceAddress\":7,\"SavedDevices\":[{\"Name\":\"Saved\",\"Address\":7}]}";
        File.WriteAllText(path, json);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Equal(LevelControlMode.Slider, loaded.VolumeControlMode);
        Assert.Equal(LevelControlMode.Slider, loaded.SubwooferControlMode);
        Assert.True(loaded.IsSetupComplete);
        Assert.Equal("en", loaded.Language);
        Assert.Equal("Dark", loaded.Theme);
        Assert.True(loaded.UseReadOnlyValueFields);
        Assert.False(loaded.ShowHints);
        Assert.False(loaded.AutoApplySelections);
        Assert.True(loaded.AutoConnectAtStartup);
        Assert.True(loaded.PowerOnAtStartup);
        Assert.Equal(7UL, loaded.StartupDeviceAddress);
        Assert.Equal(new SavedSoundbar("Saved", 7), Assert.Single(loaded.SavedDevices));
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("\"Unknown\"")]
    [InlineData("\"\"")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"0\"")]
    [InlineData("\"1\"")]
    [InlineData("\"2\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void InvalidLevelControlValuesAreRejectedWithoutOverwriting(string value)
    {
        foreach (string property in new[] { "VolumeControlMode", "SubwooferControlMode" })
        {
            string path = ConfigurationTests.TestPath($"level-controls-invalid-{property}.json");
            string json = "{\"" + property + "\":" + value + "}";
            File.WriteAllText(path, json);
            UiPreferences loaded = UiPreferences.Load(out string? warning, path);
            Assert.NotNull(warning);
            Assert.Equal(LevelControlMode.Slider, loaded.VolumeControlMode);
            Assert.Equal(LevelControlMode.Slider, loaded.SubwooferControlMode);
            Assert.Equal(json, File.ReadAllText(path));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void UndefinedLevelControlValuesCannotReplaceValidConfiguration(int value)
    {
        string path = ConfigurationTests.TestPath($"level-controls-invalid-save-{value}.json");
        UiPreferences original = new() { VolumeControlMode = LevelControlMode.Slider, SubwooferControlMode = LevelControlMode.Slider };
        Assert.True(original.TrySave(out _, path));
        byte[] originalBytes = File.ReadAllBytes(path);
        foreach (UiPreferences invalid in new[]
        {
            original with { VolumeControlMode = (LevelControlMode)value },
            original with { SubwooferControlMode = (LevelControlMode)value }
        })
        {
            Assert.False(invalid.TrySave(out string? warning, path));
            Assert.NotNull(warning);
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
        }
    }

    [Fact]
    public void FailedSavePreservesBothLevelControlModes()
    {
        string path = ConfigurationTests.TestPath("level-controls-locked.json");
        UiPreferences original = new() { VolumeControlMode = LevelControlMode.Buttons, SubwooferControlMode = LevelControlMode.Slider };
        Assert.True(original.TrySave(out _, path));
        byte[] originalBytes = File.ReadAllBytes(path);
        UiPreferences candidate = original with { VolumeControlMode = LevelControlMode.Slider, SubwooferControlMode = LevelControlMode.Buttons };
        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(candidate.TrySave(out string? warning, path));
            Assert.NotNull(warning);
        }
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        UiPreferences loaded = UiPreferences.Load(out string? loadWarning, path);
        Assert.Null(loadWarning);
        Assert.Equal(original.VolumeControlMode, loaded.VolumeControlMode);
        Assert.Equal(original.SubwooferControlMode, loaded.SubwooferControlMode);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
    }
}
