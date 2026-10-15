using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class ConfigurationRegressionTests
{
    private static readonly SoundbarIdentity VerifiedIdentity = new("SR-C30A", "1.00", 0x27, 0);

    public ConfigurationRegressionTests()
    {
        Localization.Initialize();
        Localization.SetLanguage("pl");
    }

    [Fact]
    public void DefaultConfigurationPathIsBesideTheApplication()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "config.json"), UiPreferences.GetFilePath());
    }

    [Fact]
    public void MissingConfigurationUsesDefaultsWithoutCreatingAFile()
    {
        string path = RegressionPath("missing.json");
        Assert.False(File.Exists(path));

        UiPreferences loaded = UiPreferences.Load(out string? warning, path);

        Assert.Null(warning);
        AssertDefaults(loaded);
        Assert.False(File.Exists(path));
        AssertNoTemporaryFiles(path);
    }

    [Fact]
    public void LegacySchemaCanBeTransferredWithoutChangingItsSource()
    {
        string sourcePath = RegressionPath("legacy-source.json");
        string destinationPath = RegressionPath("migrated-config.json");
        const string legacyJson = """
            {"SchemaVersion":1,"Theme":"Dark","Language":"en","FirstRunCompleted":true,
             "ShowHints":false,"AutoApplySelections":false,"HiddenRows":["Power","Mute"],
             "SavedDevices":[{"Name":"Legacy SR-C30A","Address":1}]}
            """;
        byte[] sourceBytes = Encoding.UTF8.GetBytes(legacyJson);
        File.WriteAllBytes(sourcePath, sourceBytes);

        UiPreferences legacy = UiPreferences.Load(out string? sourceWarning, sourcePath);

        Assert.Null(sourceWarning);
        Assert.True(legacy.IsSetupComplete);
        Assert.Equal("en", legacy.Language);
        Assert.Equal("Dark", legacy.Theme);
        Assert.False(legacy.ShowHints);
        Assert.False(legacy.AutoApplySelections);
        Assert.Equal(new[] { ControlRow.Power, ControlRow.Mute }, legacy.HiddenRows);
        Assert.Equal(new SavedSoundbar("Legacy SR-C30A", 1), Assert.Single(legacy.SavedDevices));
        Assert.Equal(100, legacy.TextScale);
        Assert.False(legacy.CloseToTray);

        Assert.True(legacy.TrySave(out string? saveWarning, destinationPath));
        Assert.Null(saveWarning);
        UiPreferences migrated = UiPreferences.Load(out string? destinationWarning, destinationPath);
        Assert.Null(destinationWarning);
        Assert.Equal(JsonSerializer.Serialize(legacy), JsonSerializer.Serialize(migrated));
        Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(destinationPath));
        Assert.Equal(100, saved.RootElement.GetProperty("TextScale").GetInt32());
        Assert.False(saved.RootElement.GetProperty("CloseToTray").GetBoolean());
        AssertNoTemporaryFiles(destinationPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    [InlineData("{")]
    [InlineData("{\"Language\":\"en\",\"FirstRunCompleted\":true")]
    [InlineData("{\"TextScale\":150,\"CloseToTray\":true,\"SavedDevices\":[")]
    [InlineData("{\"SavedDevices\":[{\"Name\":\"Yamaha")]
    public void TruncatedJsonNeverPartiallyAppliesSettings(string json)
    {
        AssertRejectedJson("truncated.json", json);
    }

    [Fact]
    public void TruncatedUtf8DeviceNameIsRejectedWithoutChangingTheBytes()
    {
        string path = RegressionPath("truncated-utf8.json");
        byte[] completePrefix = Encoding.UTF8.GetBytes("{\"SavedDevices\":[{\"Name\":\"\u017C");
        byte[] truncated = completePrefix[..^1];
        File.WriteAllBytes(path, truncated);

        UiPreferences loaded = UiPreferences.Load(out string? warning, path);

        Assert.False(string.IsNullOrWhiteSpace(warning));
        AssertDefaults(loaded);
        Assert.Equal(truncated, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(101, true)]
    [InlineData(150, false)]
    [InlineData(199, true)]
    [InlineData(200, false)]
    [InlineData(200, true)]
    public void NewPreferencesRoundTripIncludingUnalignedScaleValues(int textScale, bool closeToTray)
    {
        string path = RegressionPath("new-preferences.json");
        UiPreferences original = new()
        {
            Language = "en", Theme = "Dark", FirstRunCompleted = true,
            TextScale = textScale, CloseToTray = closeToTray
        };

        Assert.True(original.TrySave(out string? saveWarning, path));
        Assert.Null(saveWarning);
        UiPreferences loaded = UiPreferences.Load(out string? loadWarning, path);

        Assert.Null(loadWarning);
        Assert.True(loaded.IsSetupComplete);
        Assert.Equal(textScale, loaded.TextScale);
        Assert.Equal(closeToTray, loaded.CloseToTray);
        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(textScale, saved.RootElement.GetProperty("TextScale").GetInt32());
        Assert.Equal(closeToTray, saved.RootElement.GetProperty("CloseToTray").GetBoolean());
        AssertNoTemporaryFiles(path);
    }

    [Theory]
    [InlineData("\"TextScale\":99")]
    [InlineData("\"TextScale\":201")]
    [InlineData("\"TextScale\":-2147483648")]
    [InlineData("\"TextScale\":2147483648")]
    [InlineData("\"TextScale\":100.5")]
    [InlineData("\"TextScale\":\"150\"")]
    [InlineData("\"TextScale\":null")]
    [InlineData("\"TextScale\":true")]
    [InlineData("\"CloseToTray\":\"true\"")]
    [InlineData("\"CloseToTray\":1")]
    [InlineData("\"CloseToTray\":null")]
    [InlineData("\"CloseToTray\":[]")]
    public void InvalidNewPreferencesRejectTheWholeConfiguration(string invalidProperty)
    {
        string json = "{\"Language\":\"en\",\"Theme\":\"Dark\",\"FirstRunCompleted\":true," + invalidProperty + "}";
        AssertRejectedJson("invalid-new-preference.json", json);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(99)]
    [InlineData(201)]
    [InlineData(int.MaxValue)]
    public void InvalidTextScaleCannotReplaceAnExistingConfiguration(int textScale)
    {
        string path = RegressionPath("invalid-scale-save.json");
        const string existing = "{\"Language\":\"en\",\"TextScale\":150,\"CloseToTray\":true}";
        File.WriteAllText(path, existing);

        Assert.False(new UiPreferences { TextScale = textScale }.TrySave(out string? warning, path));

        Assert.False(string.IsNullOrWhiteSpace(warning));
        Assert.Equal(existing, File.ReadAllText(path));
        AssertNoTemporaryFiles(path);
    }

    [Fact]
    public void ExclusivelyLockedConfigurationReportsReadFailureWithoutChangingTheFile()
    {
        string path = RegressionPath("read-locked.json");
        const string existing = "{\"Language\":\"en\",\"TextScale\":150}";
        File.WriteAllText(path, existing);

        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            UiPreferences loaded = UiPreferences.Load(out string? warning, path);
            Assert.False(string.IsNullOrWhiteSpace(warning));
            AssertDefaults(loaded);
        }

        Assert.Equal(existing, File.ReadAllText(path));
        AssertNoTemporaryFiles(path);
    }

    [Fact]
    public void ReadOnlyConfigurationCannotBeReplacedAndTemporaryFilesAreRemoved()
    {
        string path = RegressionPath("read-only.json");
        const string existing = "{\"Language\":\"en\",\"TextScale\":150}";
        File.WriteAllText(path, existing);
        FileAttributes originalAttributes = File.GetAttributes(path);
        try
        {
            File.SetAttributes(path, originalAttributes | FileAttributes.ReadOnly);

            Assert.False(new UiPreferences { CloseToTray = true }.TrySave(out string? warning, path));

            Assert.False(string.IsNullOrWhiteSpace(warning));
            Assert.Equal(existing, File.ReadAllText(path));
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
            AssertNoTemporaryFiles(path);
        }
        finally
        {
            File.SetAttributes(path, originalAttributes);
        }
    }

    [Fact]
    public void AFileInTheParentPathReportsSaveFailureWithoutModifyingIt()
    {
        string parentPath = RegressionPath("parent-is-file");
        const string existing = "This file must not become a directory.";
        File.WriteAllText(parentPath, existing);
        string path = Path.Combine(parentPath, "config.json");

        Assert.False(new UiPreferences().TrySave(out string? warning, path));

        Assert.False(string.IsNullOrWhiteSpace(warning));
        Assert.Equal(existing, File.ReadAllText(parentPath));
        Assert.False(Directory.Exists(parentPath));
        Assert.False(File.Exists(path));
        AssertNoTemporaryFiles(parentPath);
    }

    [Fact]
    public void DirectoryAtTheDestinationIsNotReplacedAndTemporaryFilesAreRemoved()
    {
        string path = RegressionPath("destination-is-directory.json");
        Directory.CreateDirectory(path);
        string markerPath = Path.Combine(path, "marker.txt");
        File.WriteAllText(markerPath, "preserve directory contents");

        Assert.False(new UiPreferences().TrySave(out string? warning, path));

        Assert.False(string.IsNullOrWhiteSpace(warning));
        Assert.True(Directory.Exists(path));
        Assert.Equal("preserve directory contents", File.ReadAllText(markerPath));
        AssertNoTemporaryFiles(path);
    }

    public static TheoryData<string> RejectedDeviceNames => new()
    {
        "Yamaha\0injected", "Yamaha\rinjected", "Yamaha\ninjected", "Yamaha\tinjected",
        "Yamaha\u001Binjected", "Yamaha\u007Finjected", new string('x', 257)
    };

    [Theory]
    [MemberData(nameof(RejectedDeviceNames))]
    public void UntrustedDeviceNamesAreRejectedOnImportAndBeforeRemembering(string name)
    {
        string json = JsonSerializer.Serialize(new { SavedDevices = new[] { new SavedSoundbar(name, 1) } });
        AssertRejectedJson("invalid-device-name.json", json);
        UiPreferences original = new() { SavedDevices = [new SavedSoundbar("Existing", 1)] };

        Assert.Throws<JsonException>(() => original.Remember(VerifiedIdentity, new(name, 1, -40)));

        Assert.Equal(new SavedSoundbar("Existing", 1), Assert.Single(original.SavedDevices));
    }

    [Theory]
    [InlineData("..\\..\\config.json")]
    [InlineData("\"}],\"CloseToTray\":true,\"SavedDevices\":[{\"Name\":\"")]
    [InlineData("<script>alert('name')</script> & {Binding Password}")]
    public void DeviceNameMetacharactersRemainLiteralData(string name)
    {
        string path = RegressionPath("literal-device-name.json");
        UiPreferences original = new UiPreferences().Remember(VerifiedIdentity, new(name, 1, -40));

        Assert.True(original.TrySave(out string? saveWarning, path));
        Assert.Null(saveWarning);
        UiPreferences loaded = UiPreferences.Load(out string? loadWarning, path);

        Assert.Null(loadWarning);
        Assert.Equal(new SavedSoundbar(name, 1), Assert.Single(loaded.SavedDevices));
        Assert.False(loaded.CloseToTray);
        Assert.Equal(100, loaded.TextScale);
        AssertNoTemporaryFiles(path);
    }

    [Theory]
    [InlineData(1024 * 1024, true)]
    [InlineData(1024 * 1024 + 1, false)]
    public void ConfigurationReadLimitIncludesExactlyOneMegabyte(int length, bool accepted)
    {
        string path = RegressionPath("size-boundary.json");
        byte[] bytes = Encoding.UTF8.GetBytes("{}" + new string(' ', length - 2));
        File.WriteAllBytes(path, bytes);

        UiPreferences loaded = UiPreferences.Load(out string? warning, path);

        if (accepted)
            Assert.Null(warning);
        else
            Assert.False(string.IsNullOrWhiteSpace(warning));
        AssertDefaults(loaded);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void SuccessfulSaveMustBeReloadableAtSupportedDeviceAndNameLimits()
    {
        string path = RegressionPath("maximum-escaped-device-names.json");
        const string existing = "{\"Language\":\"en\",\"CloseToTray\":true}";
        File.WriteAllText(path, existing);
        UiPreferences original = new()
        {
            Language = "en", Theme = "Dark", FirstRunCompleted = true,
            TextScale = 175, CloseToTray = true,
            SavedDevices = Enumerable.Range(1, 1024)
                .Select(address => new SavedSoundbar(new string('\u017C', 256), (ulong)address)).ToArray()
        };

        bool saved = original.TrySave(out string? saveWarning, path);

        AssertNoTemporaryFiles(path);
        if (!saved)
        {
            Assert.False(string.IsNullOrWhiteSpace(saveWarning));
            Assert.Equal(existing, File.ReadAllText(path));
            return;
        }

        Assert.Null(saveWarning);
        UiPreferences loaded = UiPreferences.Load(out string? loadWarning, path);
        Assert.Null(loadWarning);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(loaded));
    }

    [Fact]
    public void TooManySavedDevicesCannotOverwriteAnExistingConfiguration()
    {
        string path = RegressionPath("too-many-devices.json");
        const string existing = "{\"Language\":\"en\"}";
        File.WriteAllText(path, existing);
        UiPreferences oversized = new()
        {
            SavedDevices = Enumerable.Range(1, 1025)
                .Select(address => new SavedSoundbar("Yamaha", (ulong)address)).ToArray()
        };

        Assert.False(oversized.TrySave(out string? warning, path));

        Assert.False(string.IsNullOrWhiteSpace(warning));
        Assert.Equal(existing, File.ReadAllText(path));
        AssertNoTemporaryFiles(path);
    }

    [Fact]
    public async Task ConcurrentWritersLeaveOneWholeSuccessfulSnapshotAndNoTemporaryFiles()
    {
        string path = RegressionPath("concurrent-writers.json");
        File.WriteAllText(path, "{}");
        UiPreferences[] candidates = Enumerable.Range(0, 8).Select(index => new UiPreferences
        {
            Language = index % 2 == 0 ? "pl" : "en",
            Theme = index % 2 == 0 ? "Light" : "Dark",
            FirstRunCompleted = true, TextScale = 100 + index, CloseToTray = index % 2 != 0,
            HiddenRows = [index % 2 == 0 ? ControlRow.Power : ControlRow.Mute],
            SavedDevices = [new SavedSoundbar($"Writer {index}", (ulong)index + 1)]
        }).ToArray();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(UiPreferences Preferences, bool Saved, string? Warning)>[] writers = candidates.Select(preferences => Task.Run(async () =>
        {
            await start.Task;
            bool saved = preferences.TrySave(out string? warning, path);
            return (Preferences: preferences, Saved: saved, Warning: warning);
        })).ToArray();

        start.SetResult();
        var results = await Task.WhenAll(writers);

        Assert.Contains(results, result => result.Saved);
        foreach (var result in results)
        {
            if (result.Saved)
                Assert.Null(result.Warning);
            else
                Assert.False(string.IsNullOrWhiteSpace(result.Warning));
        }
        UiPreferences loaded = UiPreferences.Load(out string? loadWarning, path);
        Assert.Null(loadWarning);
        string actualSnapshot = JsonSerializer.Serialize(loaded);
        Assert.Contains(results, result => result.Saved && JsonSerializer.Serialize(result.Preferences) == actualSnapshot);
        AssertNoTemporaryFiles(path);
    }

    private static string RegressionPath(string name) => ConfigurationTests.TestPath(Path.Combine("regression", name));

    private static void AssertRejectedJson(string name, string json)
    {
        string path = RegressionPath(name);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        File.WriteAllBytes(path, bytes);

        UiPreferences loaded = UiPreferences.Load(out string? warning, path);

        Assert.False(string.IsNullOrWhiteSpace(warning));
        AssertDefaults(loaded);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        AssertNoTemporaryFiles(path);
    }

    private static void AssertDefaults(UiPreferences preferences)
    {
        Assert.Equal(1, preferences.SchemaVersion);
        Assert.Equal("System", preferences.Theme);
        Assert.Null(preferences.Language);
        Assert.False(preferences.FirstRunCompleted);
        Assert.False(preferences.IsSetupComplete);
        Assert.Equal(100, preferences.TextScale);
        Assert.False(preferences.CloseToTray);
        Assert.True(preferences.ConfirmExit);
        Assert.True(preferences.AutoApplySelections);
        Assert.True(preferences.ShowHints);
        Assert.True(preferences.AutoConnectAtStartup);
        Assert.False(preferences.PowerOnAtStartup);
        Assert.False(preferences.PowerOffOnExit);
        Assert.Null(preferences.StartupDeviceAddress);
        Assert.Equal(LevelControlMode.Slider, preferences.VolumeControlMode);
        Assert.Equal(LevelControlMode.Slider, preferences.SubwooferControlMode);
        Assert.Empty(preferences.SavedDevices);
        Assert.Empty(preferences.HiddenRows);
    }

    private static void AssertNoTemporaryFiles(string path)
    {
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
    }
}
