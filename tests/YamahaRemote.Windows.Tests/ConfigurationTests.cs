using System.IO;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class ConfigurationTests
{
    private static readonly SoundbarIdentity VerifiedIdentity = new("SR-C30A", "1.00", 0x27, 0);

    public ConfigurationTests()
    {
        Localization.Initialize();
        Localization.SetLanguage("pl");
    }

    internal static string TestPath(string name)
    {
        return TestPaths.ValidationFile("config-tests", name);
    }

    [Fact]
    public void ConfigurationRoundTripsAllPreferencesAndDevices()
    {
        string path = TestPath("roundtrip.json");
        UiPreferences original = new UiPreferences
        {
            FirstRunCompleted = true, Language = "en", Theme = "Dark", ShowHints = false,
            AutoApplySelections = false, HiddenRows = [ControlRow.Power, ControlRow.Mute]
        }.Remember(VerifiedIdentity, new("Simulated soundbar", 0x020000000001, -24));
        Assert.True(original.TrySave(out string? saveWarning, path));
        Assert.Null(saveWarning);
        UiPreferences loaded = UiPreferences.Load(out string? loadWarning, path);
        Assert.Null(loadWarning);
        Assert.True(loaded.IsSetupComplete);
        Assert.Equal("en", loaded.Language);
        Assert.Equal("Dark", loaded.Theme);
        Assert.False(loaded.ShowHints);
        Assert.False(loaded.AutoApplySelections);
        Assert.Equal(original.HiddenRows, loaded.HiddenRows);
        Assert.Equal(original.SavedDevices, loaded.SavedDevices);
        Assert.DoesNotContain("SignalStrength", File.ReadAllText(path));
        Assert.DoesNotContain("IsSetupComplete", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"FirstRunCompleted\":true}")]
    [InlineData("{\"Language\":\"en\",\"Theme\":\"Dark\"}")]
    public void MissingSetupChoicesNeverSkipTheDialog(string json)
    {
        string path = TestPath("incomplete.json");
        File.WriteAllText(path, json);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.False(loaded.IsSetupComplete);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"SchemaVersion\":2}")]
    [InlineData("{\"Theme\":\"Invalid\"}")]
    [InlineData("{\"Language\":\"de\"}")]
    [InlineData("{\"HiddenRows\":null}")]
    [InlineData("{\"HiddenRows\":[\"Unknown\"]}")]
    [InlineData("{\"SavedDevices\":null}")]
    [InlineData("{\"SavedDevices\":[null]}")]
    [InlineData("{\"SavedDevices\":[{\"Name\":\"Yamaha\",\"Address\":0}]}")]
    [InlineData("{\"SavedDevices\":[{\"Name\":\"\",\"Address\":1}]}")]
    [InlineData("{\"SavedDevices\":[{\"Name\":\"Yamaha\",\"Address\":281474976710656}]}")]
    [InlineData("{\"SavedDevices\":[{\"Name\":\"One\",\"Address\":1},{\"Name\":\"Two\",\"Address\":1}]}")]
    public void InvalidConfigurationIsReportedWithoutOverwritingIt(string json)
    {
        string path = TestPath("invalid.json");
        File.WriteAllText(path, json);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.NotNull(warning);
        Assert.False(loaded.IsSetupComplete);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void RememberUpdatesByAddressAndRemoveOnlyAffectsLocalList()
    {
        UiPreferences preferences = new UiPreferences()
            .Remember(VerifiedIdentity, new("Original", 1, -40))
            .Remember(VerifiedIdentity, new("Updated", 1, -60))
            .Remember(VerifiedIdentity, new("Updated", 2, -30));
        Assert.Equal(2, preferences.SavedDevices.Length);
        Assert.All(preferences.SavedDevices, device => Assert.Equal("Updated", device.Name));
        UiPreferences removed = preferences.RemoveDevice(1);
        Assert.Equal(2UL, Assert.Single(removed.SavedDevices).Address);
        Assert.Equal(2, preferences.SavedDevices.Length);
        Assert.Empty(removed.RemoveDevice(2).SavedDevices);
        Assert.Equal("SR-C30A", Assert.Single(new UiPreferences().Remember(VerifiedIdentity, new("", 3, -40)).SavedDevices).Name);
        Assert.Throws<ArgumentException>(() => preferences.Remember(VerifiedIdentity with { ModelCode = 0x28 }, new("Other", 3, -40)));
    }

    [Fact]
    public void FailedAtomicSavePreservesExistingFile()
    {
        string path = TestPath("locked.json");
        File.WriteAllText(path, "{}");
        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(new UiPreferences { Language = "en" }.TrySave(out _, path));
        Assert.Equal("{}", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "locked.json.*.tmp"));
        Assert.False(new UiPreferences { Theme = "invalid" }.TrySave(out _, path));
        Assert.Equal("{}", File.ReadAllText(path));
    }

    [Fact]
    public void ConfigurationOverrideRequiresAnExplicitPath()
    {
        Assert.Null(App.ParseConfigurationPath([]));
        string path = TestPath("override.json");
        Assert.Equal(path, App.ParseConfigurationPath(["--config", path]));
        Assert.Throws<ArgumentException>(() => App.ParseConfigurationPath(["--config"]));
        Assert.Throws<ArgumentException>(() => App.ParseConfigurationPath(["--unknown", path]));
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "config.json"), UiPreferences.GetFilePath());
    }
}
