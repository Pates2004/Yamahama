using System.IO;
using System.Text.Json;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class ValueDisplayPreferencesTests
{
    [Fact]
    public void DefaultsAndMissingConfigurationUseStaticTextWithoutCreatingAFile()
    {
        UiPreferences defaults = new();
        Assert.Equal(ValueDisplayMode.StaticText, defaults.ValueDisplayMode);
        Assert.False(defaults.UseReadOnlyValueFields);
        Assert.Equal(1, defaults.SchemaVersion);

        string path = ConfigurationTests.TestPath($"value-display-missing-{Guid.NewGuid():N}.json");
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Equal(ValueDisplayMode.StaticText, loaded.ValueDisplayMode);
        Assert.False(loaded.UseReadOnlyValueFields);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("ReadOnlyFields")]
    [InlineData("StaticText")]
    [InlineData("None")]
    public void AllModesRoundTripAsStringsWithoutChangingOtherPreferences(string modeName)
    {
        ValueDisplayMode mode = Enum.Parse<ValueDisplayMode>(modeName);
        string path = ConfigurationTests.TestPath($"value-display-roundtrip-{modeName}.json");
        UiPreferences original = new()
        {
            ValueDisplayMode = mode,
            SchemaVersion = 1,
            Theme = "Dark",
            Language = "en",
            FirstRunCompleted = true,
            SavedDevices =
            [
                new SavedSoundbar("Saved Yamaha", 1) { SoundProfile = new(SoundMode.Game, true, true, -2) },
                new SavedSoundbar("Second Yamaha", 2)
            ],
            StartupDeviceAddress = 1,
            StartWithWindows = true,
            AutoConnectAtStartup = false,
            PowerOnAtStartup = true,
            PowerOffOnExit = true,
            KeepPoweredOn = true,
            HiddenRows = [ControlRow.Input, ControlRow.Mute],
            AutoApplySelections = false,
            ShowHints = false,
            ShowOperationMessages = true,
            VolumeControlMode = LevelControlMode.Buttons,
            SubwooferControlMode = LevelControlMode.Buttons,
            AnnounceStateChanges = false,
            CloseToTray = true,
            TextScale = 150
        };

        Assert.True(original.TrySave(out string? saveWarning, path));
        Assert.Null(saveWarning);
        AssertStoredMode(path, modeName);
        byte[] beforeLoad = File.ReadAllBytes(path);
        UiPreferences loaded = UiPreferences.Load(out string? loadWarning, path);
        Assert.Null(loadWarning);
        Assert.Equal(mode, loaded.ValueDisplayMode);
        Assert.Equal(mode == ValueDisplayMode.ReadOnlyFields, loaded.UseReadOnlyValueFields);
        Assert.Equal(original.HiddenRows, loaded.HiddenRows);
        Assert.Equal(original.SavedDevices.Select(device => (device.Name, device.Address, device.SoundProfile)),
            loaded.SavedDevices.Select(device => (device.Name, device.Address, device.SoundProfile)));
        Assert.Single(loaded.SavedDevices[0].SavedSoundProfiles);
        Assert.Empty(loaded.SavedDevices[1].SavedSoundProfiles);
        Assert.Equal(original, loaded with { HiddenRows = original.HiddenRows, SavedDevices = original.SavedDevices });
        Assert.Equal(beforeLoad, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("", "StaticText")]
    [InlineData("\"UseReadOnlyValueFields\":true,", "ReadOnlyFields")]
    [InlineData("\"UseReadOnlyValueFields\":false,", "StaticText")]
    [InlineData("\"usereadonlyvaluefields\":true,", "ReadOnlyFields")]
    [InlineData("\"USEREADONLYVALUEFIELDS\":false,", "StaticText")]
    public void LegacyPreferencesMigrateWithoutLosingDataOrRewritingOnLoad(string legacyProperty, string modeName)
    {
        string path = ConfigurationTests.TestPath("value-display-legacy.json");
        string json = $$$"""
            {
                {{{legacyProperty}}}
                "SchemaVersion": 1,
                "Theme": "Dark",
                "Language": "en",
                "FirstRunCompleted": true,
                "ShowHints": false,
                "ShowOperationMessages": true,
                "AnnounceStateChanges": false,
                "TextScale": 150,
                "HiddenRows": ["Input", "Volume"],
                "StartupDeviceAddress": 1,
                "PowerOnAtStartup": true,
                "SavedDevices": [{"Name": "Saved Yamaha", "Address": 1,
                    "SoundProfile": {"Mode": "Game", "ClearVoice": true, "BassExtension": false, "Subwoofer": -2}}]
            }
            """;
        File.WriteAllText(path, json);
        byte[] beforeLoad = File.ReadAllBytes(path);

        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Equal(Enum.Parse<ValueDisplayMode>(modeName), loaded.ValueDisplayMode);
        Assert.Equal(modeName == "ReadOnlyFields", loaded.UseReadOnlyValueFields);
        Assert.Equal(1, loaded.SchemaVersion);
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("en", loaded.Language);
        Assert.True(loaded.IsSetupComplete);
        Assert.False(loaded.ShowHints);
        Assert.True(loaded.ShowOperationMessages);
        Assert.False(loaded.AnnounceStateChanges);
        Assert.Equal(150, loaded.TextScale);
        Assert.Equal(new[] { ControlRow.Input, ControlRow.Volume }, loaded.HiddenRows);
        Assert.Equal((ulong?)1, loaded.StartupDeviceAddress);
        Assert.True(loaded.PowerOnAtStartup);
        SavedSoundbar savedDevice = Assert.Single(loaded.SavedDevices);
        Assert.Equal("Saved Yamaha", savedDevice.Name);
        Assert.Equal(1UL, savedDevice.Address);
        Assert.Equal(new SoundProfile(SoundMode.Game, true, false, -2), Assert.Single(savedDevice.SavedSoundProfiles).SoundProfile);
        Assert.Equal(beforeLoad, File.ReadAllBytes(path));

        Assert.True(loaded.TrySave(out string? saveWarning, path));
        Assert.Null(saveWarning);
        AssertStoredMode(path, modeName);
    }

    [Theory]
    [InlineData("ReadOnlyFields")]
    [InlineData("StaticText")]
    [InlineData("None")]
    public void ExplicitModeWinsRegardlessOfLegacyTypeKeyOrderOrCasing(string modeName)
    {
        string path = ConfigurationTests.TestPath($"value-display-precedence-{modeName}.json");
        foreach (string legacyValue in new[] { "true", "false", "null", "\"true\"", "0", "[]", "{}" })
        foreach (bool legacyFirst in new[] { true, false })
        foreach (bool mixedCasing in new[] { true, false })
        {
            string modeKey = mixedCasing ? "vAlUeDiSpLaYmOdE" : "ValueDisplayMode";
            string legacyKey = mixedCasing ? "uSeReAdOnLyVaLuEfIeLdS" : "UseReadOnlyValueFields";
            string modeValue = mixedCasing ? modeName.ToLowerInvariant() : modeName;
            string newProperty = $"\"{modeKey}\":\"{modeValue}\"";
            string oldProperty = $"\"{legacyKey}\":{legacyValue}";
            string json = "{" + (legacyFirst ? oldProperty + "," + newProperty : newProperty + "," + oldProperty) + "}";
            File.WriteAllText(path, json);
            byte[] beforeLoad = File.ReadAllBytes(path);

            UiPreferences loaded = UiPreferences.Load(out string? warning, path);
            Assert.Null(warning);
            Assert.Equal(Enum.Parse<ValueDisplayMode>(modeName), loaded.ValueDisplayMode);
            Assert.Equal(modeName == "ReadOnlyFields", loaded.UseReadOnlyValueFields);
            Assert.Equal(beforeLoad, File.ReadAllBytes(path));
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"\"")]
    [InlineData("\"Unknown\"")]
    [InlineData("\"0\"")]
    [InlineData("\"1\"")]
    [InlineData("\"2\"")]
    [InlineData("\"3\"")]
    [InlineData("\"ReadOnlyFields, StaticText\"")]
    [InlineData("\"ReadOnlyFields, None\"")]
    public void MalformedExplicitModeIsRejectedWithoutLegacyFallbackOrFileMutation(string value)
    {
        string path = ConfigurationTests.TestPath("value-display-invalid-new.json");
        foreach (bool legacyFirst in new[] { true, false })
        {
            string newProperty = $"\"vAlUeDiSpLaYmOdE\":{value}";
            string oldProperty = "\"UseReadOnlyValueFields\":true";
            string json = "{" + (legacyFirst ? oldProperty + "," + newProperty : newProperty + "," + oldProperty)
                + ",\"Language\":\"en\",\"FirstRunCompleted\":true}";
            File.WriteAllText(path, json);
            byte[] beforeLoad = File.ReadAllBytes(path);

            UiPreferences loaded = UiPreferences.Load(out string? warning, path);
            Assert.NotNull(warning);
            Assert.Equal(ValueDisplayMode.StaticText, loaded.ValueDisplayMode);
            Assert.False(loaded.UseReadOnlyValueFields);
            Assert.False(loaded.IsSetupComplete);
            Assert.Null(loaded.Language);
            Assert.Equal(beforeLoad, File.ReadAllBytes(path));
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"true\"")]
    [InlineData("\"false\"")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void MalformedLegacyValueIsRejectedWhenNoExplicitModeExists(string value)
    {
        string path = ConfigurationTests.TestPath("value-display-invalid-legacy.json");
        foreach (string key in new[] { "UseReadOnlyValueFields", "usereadonlyvaluefields", "USEREADONLYVALUEFIELDS" })
        {
            string json = $"{{\"{key}\":{value},\"Theme\":\"Dark\"}}";
            File.WriteAllText(path, json);
            byte[] beforeLoad = File.ReadAllBytes(path);

            UiPreferences loaded = UiPreferences.Load(out string? warning, path);
            Assert.NotNull(warning);
            Assert.Equal(ValueDisplayMode.StaticText, loaded.ValueDisplayMode);
            Assert.False(loaded.UseReadOnlyValueFields);
            Assert.Equal("System", loaded.Theme);
            Assert.Equal(beforeLoad, File.ReadAllBytes(path));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void SavingUndefinedModePreservesThePriorFileAndLeavesNoTemporaryFile(int value)
    {
        string path = ConfigurationTests.TestPath("value-display-invalid-save.json");
        UiPreferences original = new() { ValueDisplayMode = ValueDisplayMode.None, Theme = "Dark" };
        Assert.True(original.TrySave(out string? saveWarning, path));
        Assert.Null(saveWarning);
        byte[] beforeSave = File.ReadAllBytes(path);

        UiPreferences invalid = original with { ValueDisplayMode = (ValueDisplayMode)value };
        Assert.False(invalid.TrySave(out string? warning, path));
        Assert.NotNull(warning);
        Assert.Equal(beforeSave, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
        UiPreferences loaded = UiPreferences.Load(out string? loadWarning, path);
        Assert.Null(loadWarning);
        Assert.Equal(ValueDisplayMode.None, loaded.ValueDisplayMode);
        Assert.Equal("Dark", loaded.Theme);
    }

    [Theory]
    [InlineData("ReadOnlyFields")]
    [InlineData("StaticText")]
    [InlineData("None")]
    public void RecordClonesAndCompatibilityInitializersKeepASingleModeState(string modeName)
    {
        ValueDisplayMode mode = Enum.Parse<ValueDisplayMode>(modeName);
        UiPreferences original = new() { ValueDisplayMode = mode, ShowHints = false };
        UiPreferences clone = original with { };
        Assert.NotSame(original, clone);
        Assert.Equal(original, clone);
        Assert.Equal(mode, clone.ValueDisplayMode);
        Assert.Equal(mode == ValueDisplayMode.ReadOnlyFields, clone.UseReadOnlyValueFields);

        UiPreferences unrelatedChange = original with { ShowHints = true };
        Assert.Equal(mode, unrelatedChange.ValueDisplayMode);
        Assert.Equal(original.UseReadOnlyValueFields, unrelatedChange.UseReadOnlyValueFields);
        Assert.False(original.ShowHints);

        UiPreferences fields = original with { UseReadOnlyValueFields = true };
        UiPreferences text = original with { UseReadOnlyValueFields = false };
        UiPreferences none = fields with { ValueDisplayMode = ValueDisplayMode.None };
        Assert.Equal(ValueDisplayMode.ReadOnlyFields, fields.ValueDisplayMode);
        Assert.True(fields.UseReadOnlyValueFields);
        Assert.Equal(ValueDisplayMode.StaticText, text.ValueDisplayMode);
        Assert.False(text.UseReadOnlyValueFields);
        Assert.Equal(ValueDisplayMode.None, none.ValueDisplayMode);
        Assert.False(none.UseReadOnlyValueFields);
        Assert.Equal(original with { ValueDisplayMode = ValueDisplayMode.ReadOnlyFields }, fields);
        Assert.Equal(original with { ValueDisplayMode = ValueDisplayMode.StaticText }, text);
        Assert.Equal(original with { ValueDisplayMode = ValueDisplayMode.None }, none);
        Assert.Equal(mode, original.ValueDisplayMode);
    }

    private static void AssertStoredMode(string path, string modeName)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = document.RootElement;
        Assert.Equal(JsonValueKind.String, root.GetProperty("ValueDisplayMode").ValueKind);
        Assert.Equal(modeName, root.GetProperty("ValueDisplayMode").GetString());
        Assert.Equal(1, root.GetProperty("SchemaVersion").GetInt32());
        Assert.DoesNotContain(root.EnumerateObject(), property =>
            property.Name.Equals("UseReadOnlyValueFields", StringComparison.OrdinalIgnoreCase));
    }
}
