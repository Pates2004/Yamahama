using System.IO;
using System.Text.Json;
using Xunit;

namespace YamahaRemote.Windows.Tests;

public sealed class HotkeyPreferencesTests
{
    public HotkeyPreferencesTests()
    {
        Localization.Initialize();
    }

    [Fact]
    public void FreshPreferencesHaveAllDefaultBindingsAndAreDisabled()
    {
        UiPreferences preferences = new();

        Assert.False(preferences.GlobalHotkeysEnabled);
        Assert.Equal(HotkeyDefaults.Create(), preferences.Hotkeys);
        Assert.Equal(Enum.GetValues<HotkeyAction>().Length, preferences.Hotkeys.Length);
        Assert.True(preferences.ValidateHotkeys(out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MasterSwitchAndBindingsRoundTripIndependently(bool enabled)
    {
        string path = ConfigurationTests.TestPath($"hotkeys-roundtrip-{enabled}.json");
        UiPreferences original = new()
        {
            GlobalHotkeysEnabled = enabled,
            Hotkeys = [new(HotkeyAction.TogglePower, "Ctrl+Alt+Shift+P"),
                new(HotkeyAction.ShowWindow, "Ctrl+Win+F2"), new(HotkeyAction.ToggleMute, "")]
        };

        Assert.True(original.TrySave(out var saveWarning, path));
        Assert.Null(saveWarning);
        UiPreferences loaded = UiPreferences.Load(out var loadWarning, path);

        Assert.Null(loadWarning);
        Assert.Equal(enabled, loaded.GlobalHotkeysEnabled);
        Assert.Equal(original.Hotkeys, loaded.Hotkeys);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("TogglePower", json.RootElement.GetProperty("Hotkeys")[0].GetProperty("Action").GetString());
        Assert.Equal("ShowWindow", json.RootElement.GetProperty("Hotkeys")[1].GetProperty("Action").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SchemaVersion\":1,\"Language\":\"en\",\"Theme\":\"Dark\",\"ShowHints\":false}")]
    public void LegacyConfigurationUsesDefaultsWithoutRewritingTheFile(string json)
    {
        string path = ConfigurationTests.TestPath("hotkeys-legacy.json");
        File.WriteAllText(path, json);
        byte[] before = File.ReadAllBytes(path);

        UiPreferences loaded = UiPreferences.Load(out var warning, path);

        Assert.Null(warning);
        Assert.False(loaded.GlobalHotkeysEnabled);
        Assert.Equal(HotkeyDefaults.Create(), loaded.Hotkeys);
        Assert.Equal(before, File.ReadAllBytes(path));
        if (json.Contains("Dark", StringComparison.Ordinal))
        {
            Assert.Equal("Dark", loaded.Theme);
            Assert.Equal("en", loaded.Language);
            Assert.False(loaded.ShowHints);
        }
    }

    [Theory]
    [InlineData("{\"Hotkeys\":null}")]
    [InlineData("{\"Hotkeys\":[null]}")]
    [InlineData("{\"Hotkeys\":[{\"Action\":\"TogglePower\",\"Gesture\":null}]}")]
    [InlineData("{\"Hotkeys\":[{\"Action\":\"TogglePower\"}]}")]
    [InlineData("{\"Hotkeys\":[{\"Gesture\":\"Ctrl+P\"}]}")]
    [InlineData("{\"Hotkeys\":[{\"Action\":0,\"Gesture\":\"Ctrl+P\"}]}")]
    [InlineData("{\"Hotkeys\":[{\"Action\":999,\"Gesture\":\"Ctrl+P\"}]}")]
    [InlineData("{\"Hotkeys\":[{\"Action\":\"999\",\"Gesture\":\"Ctrl+P\"}]}")]
    [InlineData("{\"Hotkeys\":[{\"Action\":\"UnknownAction\",\"Gesture\":\"Ctrl+P\"}]}")]
    [InlineData("{\"Hotkeys\":[{\"Action\":\"TogglePower\",\"Gesture\":\"Shift+P\"}]}")]
    [InlineData("{\"Hotkeys\":[{\"Action\":\"TogglePower\",\"Gesture\":\"Ctrl+Alt+Delete\"}]}")]
    [InlineData("{\"Hotkeys\":[{\"Action\":\"TogglePower\",\"Gesture\":\"Ctrl+F12\"}]}")]
    [InlineData("{\"Hotkeys\":[{\"Action\":\"TogglePower\",\"Gesture\":\"\\t\"}]}")]
    [InlineData("{\"Hotkeys\":[{\"Action\":\"TogglePower\",\"Gesture\":\"Ctrl+P\\n\"}]}")]
    [InlineData("{\"Hotkeys\":[{\"Action\":\"TogglePower\",\"Gesture\":\"\"},{\"Action\":\"TogglePower\",\"Gesture\":\"Ctrl+P\"}]}")]
    [InlineData("{\"Hotkeys\":[{\"Action\":\"TogglePower\",\"Gesture\":\"Ctrl+Alt+P\"},{\"Action\":\"ToggleMute\",\"Gesture\":\"alt+control+p\"}]}")]
    [InlineData("{\"GlobalHotkeysEnabled\":\"true\"}")]
    [InlineData("{\"GlobalHotkeysEnabled\":null}")]
    public void InvalidConfigurationFallsBackWithoutChangingItsBytes(string json)
    {
        string path = ConfigurationTests.TestPath("hotkeys-invalid-load.json");
        File.WriteAllText(path, json);

        UiPreferences loaded = UiPreferences.Load(out var warning, path);

        Assert.NotNull(warning);
        Assert.False(loaded.GlobalHotkeysEnabled);
        Assert.Equal(HotkeyDefaults.Create(), loaded.Hotkeys);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void OversizedGestureAndCollectionAreRejectedWithoutChangingTheirFiles()
    {
        string oversizedGesture = "{\"Hotkeys\":[{\"Action\":\"TogglePower\",\"Gesture\":\""
            + new string(' ', 129) + "\"}]}";
        string oversizedCollection = "{\"Hotkeys\":[" + string.Join(",", Enumerable.Repeat(
            "{\"Action\":\"TogglePower\",\"Gesture\":\"\"}", 21)) + "]}";
        foreach (string json in new[] { oversizedGesture, oversizedCollection })
            InvalidConfigurationFallsBackWithoutChangingItsBytes(json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidBindingsCannotOverwriteAnExistingConfigurationEvenWhenDisabled(bool enabled)
    {
        string path = ConfigurationTests.TestPath($"hotkeys-invalid-save-{enabled}.json");
        UiPreferences original = new();
        Assert.True(original.TrySave(out _, path));
        byte[] before = File.ReadAllBytes(path);
        HotkeyBinding[][] invalidSets =
        [
            null!,
            [null!],
            [new(HotkeyAction.TogglePower, null!)],
            [new((HotkeyAction)999, "Ctrl+P")],
            [new(HotkeyAction.TogglePower, "Shift+A")],
            [new(HotkeyAction.TogglePower, "\t")],
            [new(HotkeyAction.TogglePower, new string(' ', 129))],
            [new(HotkeyAction.TogglePower, ""), new(HotkeyAction.TogglePower, "Ctrl+P")],
            [new(HotkeyAction.TogglePower, "Ctrl+P"), new(HotkeyAction.ToggleMute, "control+p")],
            Enumerable.Repeat(new HotkeyBinding(HotkeyAction.TogglePower, ""), 21).ToArray()
        ];

        foreach (HotkeyBinding[] invalid in invalidSets)
        {
            UiPreferences preferences = original with { GlobalHotkeysEnabled = enabled, Hotkeys = invalid };
            Assert.False(preferences.TrySave(out var warning, path));
            Assert.NotNull(warning);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
        }
    }

    [Fact]
    public void EmptyBindingArrayRemainsExplicitlyEmptyAfterSaveAndLoad()
    {
        string path = ConfigurationTests.TestPath("hotkeys-empty.json");
        UiPreferences preferences = new() { GlobalHotkeysEnabled = true, Hotkeys = [] };

        Assert.True(preferences.TrySave(out _, path));
        UiPreferences loaded = UiPreferences.Load(out var warning, path);

        Assert.Null(warning);
        Assert.True(loaded.GlobalHotkeysEnabled);
        Assert.Empty(loaded.Hotkeys);
    }
}
