using System.IO;
using System.Text.Json.Nodes;
using Xunit;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class ExitConfirmationPreferencesTests
{
    [Fact]
    public void NewPreferencesRequireExitConfirmationWithoutEnablingPowerOff()
    {
        UiPreferences preferences = new();
        Assert.True(preferences.ConfirmExit);
        Assert.False(preferences.CloseToTray);
        Assert.False(preferences.PowerOffOnExit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingExitConfirmationMigratesWithoutChangingExistingChoices(bool closeToTray)
    {
        string path = ConfigurationTests.TestPath($"exit-confirm-migration-{closeToTray}.json");
        UiPreferences original = new()
        {
            FirstRunCompleted = true, Language = "en", Theme = "Dark", CloseToTray = closeToTray,
            PowerOffOnExit = true, TextScale = 150, ShowHints = false,
            SavedDevices = [new("Retained device", 17)], StartupDeviceAddress = 17,
            AutoConnectAtStartup = false, ValueDisplayMode = ValueDisplayMode.None
        };
        Assert.True(original.TrySave(out _, path));
        JsonObject document = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(path)));
        Assert.True(document.Remove("ConfirmExit"));
        File.WriteAllText(path, document.ToJsonString());
        byte[] before = File.ReadAllBytes(path);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.True(loaded.ConfirmExit);
        Assert.Equal(original.Hotkeys, loaded.Hotkeys);
        Assert.Equal(original with { SavedDevices = loaded.SavedDevices, HiddenRows = loaded.HiddenRows, Hotkeys = loaded.Hotkeys }, loaded);
        Assert.Equal(original.SavedDevices, loaded.SavedDevices);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitExitConfirmationRoundTripsIndependentlyOfTrayAndPower(bool confirmExit)
    {
        foreach (bool closeToTray in new[] { false, true })
        foreach (bool powerOff in new[] { false, true })
        {
            string path = ConfigurationTests.TestPath($"exit-confirm-{confirmExit}-{closeToTray}-{powerOff}.json");
            UiPreferences original = new() { ConfirmExit = confirmExit, CloseToTray = closeToTray, PowerOffOnExit = powerOff };
            Assert.True(original.TrySave(out _, path));
            UiPreferences loaded = UiPreferences.Load(out string? warning, path);
            Assert.Null(warning);
            Assert.Equal(confirmExit, loaded.ConfirmExit);
            Assert.Equal(closeToTray, loaded.CloseToTray);
            Assert.Equal(powerOff, loaded.PowerOffOnExit);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("\"false\"")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void MalformedExitConfirmationFailsSafelyWithoutOverwriting(string value)
    {
        string path = ConfigurationTests.TestPath("exit-confirm-invalid.json");
        string json = "{\"ConfirmExit\":" + value + "}";
        File.WriteAllText(path, json);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.NotNull(warning);
        Assert.True(loaded.ConfirmExit);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void FailedSavePreservesTheExplicitExitConfirmationChoice()
    {
        string path = ConfigurationTests.TestPath("exit-confirm-save-failure.json");
        UiPreferences original = new() { ConfirmExit = false };
        Assert.True(original.TrySave(out _, path));
        byte[] before = File.ReadAllBytes(path);
        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False((original with { ConfirmExit = true }).TrySave(out string? warning, path));
            Assert.NotNull(warning);
        }
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(UiPreferences.Load(out _, path).ConfirmExit);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
    }
}
