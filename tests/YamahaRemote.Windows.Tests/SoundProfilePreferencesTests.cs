using System.IO;
using System.Text.Json;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class SoundProfilePreferencesTests
{
    private const string ValidProfileJson = "{\"Mode\":\"Game\",\"ClearVoice\":true,\"BassExtension\":false,\"Subwoofer\":2}";

    [Theory]
    [InlineData(SoundMode.Stereo, -4)]
    [InlineData(SoundMode.Standard, 0)]
    [InlineData(SoundMode.Movie3D, 4)]
    [InlineData(SoundMode.Game, 2)]
    public void ProfileRoundTripsAndRemainsBoundToDevice(SoundMode mode, int level)
    {
        string path = ConfigurationTests.TestPath("profile-roundtrip.json");
        SoundProfile profile = new(mode, true, false, level);
        UiPreferences settings = new()
        {
            SavedDevices = [new("First", 1) { SoundProfile = profile }, new("Second", 2)]
        };
        Assert.True(settings.TrySave(out _, path));
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Equal(profile, loaded.SavedDevices[0].SoundProfile);
        SavedSoundProfile named = Assert.Single(loaded.SavedDevices[0].SavedSoundProfiles);
        Assert.Equal(profile, named.SoundProfile);
        Assert.NotEqual(Guid.Empty, named.Id);
        Assert.False(string.IsNullOrWhiteSpace(named.Name));
        Assert.Null(loaded.SavedDevices[1].SoundProfile);
        Assert.Empty(loaded.SavedDevices[1].SavedSoundProfiles);
        Assert.Contains(mode.ToString(), File.ReadAllText(path));
        UiPreferences remembered = loaded.Remember(new("SR-C30A", "1.00", 0x27, 0), new("Renamed", 1, 0));
        Assert.Equal(profile, remembered.SavedDevices.Single(device => device.Address == 1).SoundProfile);
        Assert.Null(Assert.Single(remembered.RemoveDevice(1).SavedDevices).SoundProfile);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Mode\":\"Game\",\"ClearVoice\":true,\"BassExtension\":false}")]
    [InlineData("{\"Mode\":\"Game\",\"ClearVoice\":true,\"Subwoofer\":0}")]
    [InlineData("{\"Mode\":\"Game\",\"BassExtension\":true,\"Subwoofer\":0}")]
    [InlineData("{\"Mode\":\"Unknown\",\"ClearVoice\":true,\"BassExtension\":false,\"Subwoofer\":0}")]
    [InlineData("{\"Mode\":\"Future\",\"ClearVoice\":true,\"BassExtension\":false,\"Subwoofer\":0}")]
    [InlineData("{\"Mode\":1,\"ClearVoice\":true,\"BassExtension\":false,\"Subwoofer\":0}")]
    [InlineData("{\"Mode\":\"Game\",\"ClearVoice\":true,\"BassExtension\":false,\"Subwoofer\":-5}")]
    [InlineData("{\"Mode\":\"Game\",\"ClearVoice\":true,\"BassExtension\":false,\"Subwoofer\":5}")]
    public void InvalidOrIncompleteProfileDoesNotOverwriteConfiguration(string profile)
    {
        string path = ConfigurationTests.TestPath("profile-invalid.json");
        string original = "{\"SavedDevices\":[{\"Name\":\"First\",\"Address\":1,\"SoundProfile\":" + profile + "}]}";
        File.WriteAllText(path, original);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.NotNull(warning);
        Assert.Empty(loaded.SavedDevices);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void PreviousConfigurationWithoutProfilesRemainsValid()
    {
        string path = ConfigurationTests.TestPath("profile-migration.json");
        File.WriteAllText(path, "{\"SavedDevices\":[{\"Name\":\"First\",\"Address\":1}],\"SchemaVersion\":1}");
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Null(Assert.Single(loaded.SavedDevices).SoundProfile);
        Assert.Empty(Assert.Single(loaded.SavedDevices).SavedSoundProfiles);
    }

    [Fact]
    public void NamedProfilesKeepTheirIdentifiersValuesOrderAndDeviceOwnership()
    {
        string path = ConfigurationTests.TestPath("named-profile-roundtrip.json");
        SavedSoundProfile movie = new(Guid.NewGuid(), "Movie", new(SoundMode.Movie3D, true, false, 4));
        SavedSoundProfile quiet = new(Guid.NewGuid(), "Quiet", new(SoundMode.Stereo, false, true, -4));
        SavedSoundProfile other = new(movie.Id, movie.Name, new(SoundMode.Game, false, false, 0));
        UiPreferences original = new()
        {
            SavedDevices =
            [
                new("First", 1) { SavedSoundProfiles = [movie, quiet] },
                new("Second", 2) { SavedSoundProfiles = [other] }
            ]
        };
        Assert.True(original.TrySave(out _, path));
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Equal(new[] { movie, quiet }, loaded.SavedDevices[0].SavedSoundProfiles);
        Assert.Equal(other, Assert.Single(loaded.SavedDevices[1].SavedSoundProfiles));
        Assert.Equal(movie.SoundProfile, loaded.SavedDevices[0].SoundProfile);
        Assert.Null(loaded.SavedDevices[0].LegacySoundProfile);
        UiPreferences remembered = loaded.Remember(new("SR-C30A", "1.00", 0x27, 0), new("Renamed", 1, 0));
        Assert.Equal(new[] { movie, quiet }, remembered.SavedDevices.Single(device => device.Address == 1).SavedSoundProfiles);
        Assert.Equal(other, Assert.Single(Assert.Single(remembered.RemoveDevice(1).SavedDevices).SavedSoundProfiles));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.False(document.RootElement.GetProperty("SavedDevices")[0].TryGetProperty("SoundProfile", out _));
    }

    [Fact]
    public void EmptyCollectionsPreserveProfileFreeDeviceRecordEquality()
    {
        string path = ConfigurationTests.TestPath("named-profile-empty-equality.json");
        SavedSoundbar original = new("First", 1);
        UiPreferences preferences = new() { SavedDevices = [original] };
        Assert.True(preferences.TrySave(out _, path));
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Equal(original, Assert.Single(loaded.SavedDevices));
        Assert.Same(original.SavedSoundProfiles, loaded.SavedDevices[0].SavedSoundProfiles);
    }

    [Theory]
    [InlineData("SoundProfile")]
    [InlineData("soundprofile")]
    public void LegacyMigrationIsReadOnlyStableAndOnlyPersistedByAnExplicitSave(string propertyName)
    {
        string path = ConfigurationTests.TestPath("named-profile-legacy.json");
        string original = "{\"SavedDevices\":[{\"Name\":\"First\",\"Address\":1,\"" + propertyName + "\":" + ValidProfileJson + "}]}";
        File.WriteAllText(path, original);
        UiPreferences first = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        SavedSoundProfile profile = Assert.Single(Assert.Single(first.SavedDevices).SavedSoundProfiles);
        Assert.Equal("First", profile.Name);
        Assert.Equal(new SoundProfile(SoundMode.Game, true, false, 2), profile.SoundProfile);
        UiPreferences second = UiPreferences.Load(out warning, path);
        Assert.Null(warning);
        Assert.Equal(profile, Assert.Single(Assert.Single(second.SavedDevices).SavedSoundProfiles));
        Assert.Equal(original, File.ReadAllText(path));
        Assert.True(first.TrySave(out warning, path));
        Assert.Null(warning);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.False(document.RootElement.GetProperty("SavedDevices")[0].TryGetProperty("SoundProfile", out _));
        UiPreferences persisted = UiPreferences.Load(out warning, path);
        Assert.Null(warning);
        Assert.Equal(profile, Assert.Single(Assert.Single(persisted.SavedDevices).SavedSoundProfiles));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedFormatPreservesUniqueLegacyValuesWithoutDuplicatingAnExistingProfile(bool matchingValues)
    {
        string path = ConfigurationTests.TestPath("named-profile-mixed.json");
        string existingValues = matchingValues ? ValidProfileJson : ValidProfileJson.Replace("\"Game\"", "\"Stereo\"");
        string identifier = "7612acb4-1450-4a9d-8000-000000000001";
        string name = JsonSerializer.Serialize("First");
        string original = "{\"SavedDevices\":[{\"Name\":\"First\",\"Address\":1,\"SoundProfile\":" + ValidProfileJson
            + ",\"SavedSoundProfiles\":[{\"Id\":\"" + identifier + "\",\"Name\":" + name + ",\"SoundProfile\":" + existingValues + "}]}]}";
        File.WriteAllText(path, original);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        SavedSoundProfile[] profiles = Assert.Single(loaded.SavedDevices).SavedSoundProfiles;
        Assert.Equal(matchingValues ? 1 : 2, profiles.Length);
        Assert.Equal(Guid.Parse(identifier), profiles[0].Id);
        Assert.Equal("First", profiles[0].Name);
        Assert.Equal(profiles.Length, profiles.Select(profile => profile.Id).Distinct().Count());
        Assert.Equal(profiles.Length, profiles.Select(profile => profile.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(profiles, profile => profile.SoundProfile == new SoundProfile(SoundMode.Game, true, false, 2));
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Equal(profiles, Assert.Single(UiPreferences.Load(out _, path).SavedDevices).SavedSoundProfiles);
    }

    [Fact]
    public void LegacyMigrationDoesNotDependOnOrChangeTheCurrentLanguage()
    {
        string previousLanguage = Localization.CurrentLanguage;
        string path = ConfigurationTests.TestPath("named-profile-language-neutral.json");
        string original = "{\"Language\":\"en\",\"SavedDevices\":[{\"Name\":\"Studio speaker\",\"Address\":1,\"SoundProfile\":" + ValidProfileJson + "}]}";
        File.WriteAllText(path, original);
        try
        {
            Localization.SetLanguage("pl");
            UiPreferences first = UiPreferences.Load(out string? warning, path);
            Assert.Null(warning);
            Assert.Equal("pl", Localization.CurrentLanguage);
            SavedSoundProfile profile = Assert.Single(Assert.Single(first.SavedDevices).SavedSoundProfiles);
            Assert.Equal("Studio speaker", profile.Name);
            Localization.SetLanguage("en");
            UiPreferences second = UiPreferences.Load(out warning, path);
            Assert.Null(warning);
            Assert.Equal("en", Localization.CurrentLanguage);
            Assert.Equal(profile, Assert.Single(Assert.Single(second.SavedDevices).SavedSoundProfiles));
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            Localization.SetLanguage(previousLanguage);
        }
    }

    [Theory]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(256)]
    public void LegacyDeviceNamesFitProfileLimitsAndRemainDistinctAfterTruncation(int deviceNameLength)
    {
        string name = new('x', deviceNameLength);
        SoundProfile legacyValues = new(SoundMode.Game, false, true, 1);
        SavedSoundbar device = new(name, 1)
        {
            SoundProfile = legacyValues,
            SavedSoundProfiles = [new(Guid.NewGuid(), new string('x', 64), new(SoundMode.Stereo, false, false, 0))]
        };
        SavedSoundbar migrated = device.MigrateSoundProfiles();
        migrated.Validate();
        Assert.Equal(2, migrated.SavedSoundProfiles.Length);
        SavedSoundProfile legacy = migrated.SavedSoundProfiles[1];
        Assert.Equal(legacyValues, legacy.SoundProfile);
        Assert.Equal(64, legacy.Name.Length);
        Assert.EndsWith(" (2)", legacy.Name);
        Assert.Null(migrated.LegacySoundProfile);
    }

    [Fact]
    public void LegacyNameTruncationDoesNotSplitASurrogatePair()
    {
        SavedSoundbar device = new(new string('x', 63) + "\ud83c\udfb5", 1)
        {
            SoundProfile = new(SoundMode.Game, true, false, 2)
        };
        SavedSoundbar migrated = device.MigrateSoundProfiles();
        migrated.Validate();
        Assert.Equal(new string('x', 63), Assert.Single(migrated.SavedSoundProfiles).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\tName")]
    [InlineData("Name\n")]
    [InlineData("Na\0me")]
    [InlineData("Na\u007fme")]
    public void InvalidNamesAreRejected(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => SavedSoundProfile.NormalizeName(name!));
        SavedSoundProfile profile = new(Guid.NewGuid(), name!, new(SoundMode.Game, true, false, 2));
        Assert.Throws<JsonException>(profile.Validate);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    public void ValidNameBoundariesRoundTrip(int length)
    {
        string path = ConfigurationTests.TestPath("named-profile-name-boundary.json");
        string name = new('x', length);
        Assert.Equal(name, SavedSoundProfile.NormalizeName("  " + name + "  "));
        SavedSoundProfile profile = new(Guid.NewGuid(), name, new(SoundMode.Standard, false, false, 0));
        Assert.True(new UiPreferences { SavedDevices = [new("First", 1) { SavedSoundProfiles = [profile] }] }.TrySave(out _, path));
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Equal(profile, Assert.Single(Assert.Single(loaded.SavedDevices).SavedSoundProfiles));
    }

    [Fact]
    public void OverlongOrUntrimmedStoredNamesCannotOverwriteExistingData()
    {
        string path = ConfigurationTests.TestPath("named-profile-invalid-name.json");
        const string original = "{\"Theme\":\"Dark\"}";
        File.WriteAllText(path, original);
        Assert.Throws<ArgumentException>(() => SavedSoundProfile.NormalizeName(new string('x', 65)));
        foreach (string name in new[] { new string('x', 65), " Leading", "Trailing " })
        {
            UiPreferences candidate = new()
            {
                SavedDevices = [new("First", 1) { SavedSoundProfiles = [new(Guid.NewGuid(), name, new(SoundMode.Game, true, false, 2))] }]
            };
            Assert.False(candidate.TrySave(out string? warning, path));
            Assert.NotNull(warning);
            Assert.Equal(original, File.ReadAllText(path));
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"Id\":\"bad\",\"Name\":\"Movie\",\"SoundProfile\":PROFILE}]")]
    [InlineData("[{\"Id\":\"00000000-0000-0000-0000-000000000000\",\"Name\":\"Movie\",\"SoundProfile\":PROFILE}]")]
    [InlineData("[{\"Name\":\"Movie\",\"SoundProfile\":PROFILE}]")]
    [InlineData("[{\"Id\":\"7612acb4-1450-4a9d-8000-000000000001\",\"SoundProfile\":PROFILE}]")]
    [InlineData("[{\"Id\":\"7612acb4-1450-4a9d-8000-000000000001\",\"Name\":\"Movie\"}]")]
    [InlineData("[{\"Id\":\"7612acb4-1450-4a9d-8000-000000000001\",\"Name\":null,\"SoundProfile\":PROFILE}]")]
    [InlineData("[{\"Id\":\"7612acb4-1450-4a9d-8000-000000000001\",\"Name\":\"Movie\",\"SoundProfile\":null}]")]
    [InlineData("[{\"Id\":\"7612acb4-1450-4a9d-8000-000000000001\",\"Name\":\"Movie\",\"SoundProfile\":{}}]")]
    [InlineData("[{\"Id\":\"7612acb4-1450-4a9d-8000-000000000001\",\"Name\":\" Movie\",\"SoundProfile\":PROFILE}]")]
    public void MalformedCollectionsAreRejectedWithoutRewritingTheFile(string collection)
    {
        string path = ConfigurationTests.TestPath("named-profile-malformed.json");
        string original = "{\"SavedDevices\":[{\"Name\":\"First\",\"Address\":1,\"SavedSoundProfiles\":"
            + collection.Replace("PROFILE", ValidProfileJson) + "}]}";
        File.WriteAllText(path, original);
        UiPreferences loaded = UiPreferences.Load(out string? warning, path);
        Assert.NotNull(warning);
        Assert.Empty(loaded.SavedDevices);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuplicateIdentifiersOrExactNamesAreRejectedButCaseDistinctNamesAreValid(bool sameIdentifier)
    {
        SavedSoundProfile first = new(Guid.NewGuid(), "Movie", new(SoundMode.Movie3D, true, true, 3));
        SavedSoundProfile second = new(sameIdentifier ? first.Id : Guid.NewGuid(), sameIdentifier ? "Other" : first.Name, first.SoundProfile);
        SavedSoundbar device = new("First", 1) { SavedSoundProfiles = [first, second] };
        Assert.Throws<JsonException>(device.Validate);
        SavedSoundbar valid = device with { SavedSoundProfiles = [first, second with { Id = Guid.NewGuid(), Name = "movie" }] };
        valid.Validate();
    }

    [Fact]
    public void CollectionLimitIsEnforcedAndDeletingTheLastProfileDoesNotResurrectLegacyData()
    {
        string path = ConfigurationTests.TestPath("named-profile-capacity.json");
        SavedSoundProfile[] profiles = Enumerable.Range(1, SavedSoundbar.MaximumSoundProfiles)
            .Select(index => new SavedSoundProfile(Guid.NewGuid(), $"Profile {index}", new(SoundMode.Game, true, false, 2))).ToArray();
        UiPreferences maximum = new() { SavedDevices = [new("First", 1) { SavedSoundProfiles = profiles }] };
        Assert.True(maximum.TrySave(out _, path));
        Assert.Equal(profiles.Length, Assert.Single(UiPreferences.Load(out _, path).SavedDevices).SavedSoundProfiles.Length);
        string original = File.ReadAllText(path);
        SavedSoundbar tooMany = maximum.SavedDevices[0] with
        {
            SavedSoundProfiles = [.. profiles, new(Guid.NewGuid(), "Extra", profiles[0].SoundProfile)]
        };
        Assert.False((maximum with { SavedDevices = [tooMany] }).TrySave(out string? warning, path));
        Assert.NotNull(warning);
        Assert.Equal(original, File.ReadAllText(path));
        SavedSoundbar mixed = maximum.SavedDevices[0] with { SoundProfile = new(SoundMode.Stereo, false, false, -4) };
        Assert.False((maximum with { SavedDevices = [mixed] }).TrySave(out warning, path));
        Assert.NotNull(warning);
        Assert.Equal(original, File.ReadAllText(path));
        SavedSoundbar legacy = new SavedSoundbar("First", 1) { SoundProfile = profiles[0].SoundProfile }.MigrateSoundProfiles();
        UiPreferences empty = new() { SavedDevices = [legacy with { SavedSoundProfiles = [] }] };
        Assert.True(empty.TrySave(out warning, path));
        Assert.Null(warning);
        SavedSoundbar reloaded = Assert.Single(UiPreferences.Load(out warning, path).SavedDevices);
        Assert.Null(warning);
        Assert.Empty(reloaded.SavedSoundProfiles);
        Assert.Null(reloaded.SoundProfile);
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("en")]
    public void EveryFailureIsLocalized(string language)
    {
        Localization.Initialize();
        Localization.SetLanguage(language);
        foreach (SoundProfileFailure failure in Enum.GetValues<SoundProfileFailure>())
        {
            string message = MainWindow.DescribeError(new SoundProfileException(failure));
            Assert.Equal(Localization.Text("Profile.Error." + failure), message);
            Assert.DoesNotContain("Profile.Error.", message);
        }
    }
}
