using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamahaRemote.Core;

namespace YamahaRemote.Windows;

internal enum ControlRow
{
    Power,
    Input,
    Volume,
    Mute,
    Subwoofer,
    SoundMode,
    ClearVoice,
    BassExtension,
    IndicatorBrightness
}

internal enum LevelControlMode
{
    Buttons,
    Slider
}

internal enum ValueDisplayMode
{
    ReadOnlyFields,
    StaticText,
    None
}

internal sealed record UiPreferences
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        RespectRequiredConstructorParameters = true,
        Converters =
        {
            new JsonStringEnumConverter<ControlRow>(allowIntegerValues: false),
            new JsonStringEnumConverter<LevelControlMode>(allowIntegerValues: false),
            new JsonStringEnumConverter<ValueDisplayMode>(allowIntegerValues: false),
            new JsonStringEnumConverter<SoundMode>(allowIntegerValues: false)
        }
    };

    public string Theme { get; init; } = "System";
    public int SchemaVersion { get; init; } = 1;
    public string? Language { get; init; }
    public bool FirstRunCompleted { get; init; }
    public SavedSoundbar[] SavedDevices { get; init; } = [];
    public bool StartWithWindows { get; init; }
    public bool AutoConnectAtStartup { get; init; } = true;
    public bool PowerOnAtStartup { get; init; }
    public bool PowerOffOnExit { get; init; }
    public bool KeepPoweredOn { get; init; }
    public ulong? StartupDeviceAddress { get; init; }
    public ControlRow[] HiddenRows { get; init; } = [];
    public bool AutoApplySelections { get; init; } = true;
    public bool ShowHints { get; init; } = true;
    public bool ShowOperationMessages { get; init; }
    public ValueDisplayMode ValueDisplayMode { get; init; } = ValueDisplayMode.StaticText;
    public LevelControlMode VolumeControlMode { get; init; } = LevelControlMode.Slider;
    public LevelControlMode SubwooferControlMode { get; init; } = LevelControlMode.Slider;
    public bool AnnounceStateChanges { get; init; } = true;
    public bool CloseToTray { get; init; }
    public bool ConfirmExit { get; init; } = true;
    public int TextScale { get; init; } = 100;

    [JsonIgnore]
    public bool UseReadOnlyValueFields
    {
        get => ValueDisplayMode == ValueDisplayMode.ReadOnlyFields;
        init => ValueDisplayMode = value ? ValueDisplayMode.ReadOnlyFields : ValueDisplayMode.StaticText;
    }

    [JsonIgnore]
    public bool IsSetupComplete => FirstRunCompleted && Language is ("pl" or "en") && Theme is ("Light" or "Dark" or "System");

    public UiPreferences Remember(SoundbarIdentity identity, DiscoveredSoundbar device)
    {
        if (identity.ModelCode != 0x27)
            throw new ArgumentException("Only a verified SR-C30A can be remembered.", nameof(identity));
        SavedSoundbar? existingDevice = SavedDevices.FirstOrDefault(existing => existing.Address == device.Address);
        string name = string.IsNullOrWhiteSpace(device.Name) ? identity.Model : device.Name;
        SavedSoundbar saved = existingDevice is null ? new(name, device.Address) : existingDevice with { Name = name };
        saved = saved.MigrateSoundProfiles();
        saved.Validate();
        return this with
        {
            SavedDevices = SavedDevices.Where(existing => existing.Address != device.Address).Append(saved).ToArray(),
            StartupDeviceAddress = StartupDeviceAddress is null && existingDevice is null ? saved.Address : StartupDeviceAddress
        };
    }

    public UiPreferences RemoveDevice(ulong address) => this with
    {
        SavedDevices = SavedDevices.Where(device => device.Address != address).ToArray(),
        StartupDeviceAddress = StartupDeviceAddress == address ? null : StartupDeviceAddress,
        AutoConnectAtStartup = StartupDeviceAddress != address && AutoConnectAtStartup,
        PowerOnAtStartup = StartupDeviceAddress != address && PowerOnAtStartup
    };

    private void Validate()
    {
        if (SchemaVersion != 1 || Theme is not ("System" or "Light" or "Dark") || Language is not (null or "pl" or "en")
            || TextScale is < 100 or > 200
            || !Enum.IsDefined(VolumeControlMode) || !Enum.IsDefined(SubwooferControlMode)
            || !Enum.IsDefined(ValueDisplayMode)
            || HiddenRows is null || HiddenRows.Any(row => !Enum.IsDefined(row)) || SavedDevices is null || SavedDevices.Length > 1024)
            throw new JsonException("Unsupported or invalid application configuration.");
        foreach (SavedSoundbar? device in SavedDevices)
        {
            if (device is null)
                throw new JsonException("A saved device record is missing.");
            device.Validate();
        }
        if (SavedDevices.Select(device => device.Address).Distinct().Count() != SavedDevices.Length)
            throw new JsonException("Duplicate saved device addresses.");
        if (PowerOnAtStartup && StartupDeviceAddress is null ||
            StartupDeviceAddress is ulong address && !SavedDevices.Any(device => device.Address == address))
            throw new JsonException("The startup device must belong to the saved device list.");
    }

    public static UiPreferences Load(out string? warning, string? filePath = null)
    {
        warning = null;
        try
        {
            using FileStream stream = File.OpenRead(filePath ?? GetFilePath());
            if (stream.Length > 1024 * 1024)
                throw new JsonException("The application configuration exceeds the supported size.");
            using JsonDocument document = JsonDocument.Parse(stream);
            UiPreferences preferences = document.RootElement.Deserialize<UiPreferences>(SerializerOptions)
                ?? throw new JsonException();
            preferences = MigrateValueDisplayMode(preferences, document.RootElement);
            preferences.Validate();
            return preferences with
            {
                HiddenRows = preferences.HiddenRows.Distinct().ToArray(),
                SavedDevices = preferences.SavedDevices.Select(device => device.MigrateSoundProfiles()).ToArray()
            };
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new UiPreferences();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or JsonException)
        {
            warning = Localization.Text("Configuration.ReadError");
            return new UiPreferences();
        }
    }

    private static UiPreferences MigrateValueDisplayMode(UiPreferences preferences, JsonElement configuration)
    {
        bool hasExplicitMode = false;
        foreach (JsonProperty property in configuration.EnumerateObject())
        {
            if (!property.Name.Equals(nameof(ValueDisplayMode), StringComparison.OrdinalIgnoreCase))
                continue;
            if (property.Value.ValueKind != JsonValueKind.String
                || !Enum.GetNames<ValueDisplayMode>().Contains(property.Value.GetString(), StringComparer.OrdinalIgnoreCase))
                throw new JsonException("The value display mode must be a supported enum name.");
            hasExplicitMode = true;
        }
        if (hasExplicitMode)
            return preferences;

        foreach (JsonProperty property in configuration.EnumerateObject())
        {
            if (!property.Name.Equals(nameof(UseReadOnlyValueFields), StringComparison.OrdinalIgnoreCase))
                continue;
            if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new JsonException("The legacy value display preference must be a boolean.");
            preferences = preferences with { UseReadOnlyValueFields = property.Value.GetBoolean() };
        }
        return preferences;
    }

    public bool TrySave(out string? warning, string? filePath = null)
    {
        warning = null;
        string? temporaryPath = null;
        try
        {
            Validate();
            UiPreferences persisted = this with
            {
                SavedDevices = SavedDevices.Select(device => device.MigrateSoundProfiles()).ToArray()
            };
            string path = Path.GetFullPath(filePath ?? GetFilePath());
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, persisted, SerializerOptions);
                if (stream.Length > 1024 * 1024)
                    throw new JsonException("The serialized configuration exceeds the supported size.");
                stream.Flush(true);
            }
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or JsonException)
        {
            warning = Localization.Text("Configuration.SaveError");
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
                {
                    System.Diagnostics.Trace.WriteLine(exception);
                }
            }
        }
    }

    internal static string GetFilePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "config.json");
    }
}

internal sealed record SavedSoundbar(string Name, ulong Address)
{
    internal const int MaximumSoundProfiles = 128;

    private SoundProfile? _legacySoundProfile;

    public SavedSoundProfile[] SavedSoundProfiles { get; init; } = [];

    [JsonIgnore]
    public SoundProfile? SoundProfile
    {
        get => _legacySoundProfile ?? SavedSoundProfiles?.FirstOrDefault()?.SoundProfile;
        init => _legacySoundProfile = value;
    }

    [JsonPropertyName(nameof(SoundProfile))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SoundProfile? LegacySoundProfile
    {
        get => _legacySoundProfile;
        init => _legacySoundProfile = value;
    }

    internal SavedSoundbar MigrateSoundProfiles()
    {
        Validate();
        if (_legacySoundProfile is null)
            return SavedSoundProfiles.Length == 0 ? this with { SavedSoundProfiles = [] } : this;
        SavedSoundProfile[] profiles = SavedSoundProfiles;
        if (!profiles.Any(profile => profile.SoundProfile == _legacySoundProfile))
        {
            if (profiles.Length >= MaximumSoundProfiles)
                throw new JsonException("The legacy sound profile would exceed the collection limit.");
            string legacyName = Name.Trim();
            string name = TruncateLegacyName(legacyName, SavedSoundProfile.MaximumNameLength);
            for (int suffix = 2; profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.Ordinal)); suffix++)
            {
                string ending = $" ({suffix})";
                name = TruncateLegacyName(legacyName, SavedSoundProfile.MaximumNameLength - ending.Length) + ending;
            }
            int identifierSuffix = 0x8000;
            Guid identifier;
            do
                identifier = Guid.Parse($"7612acb4-1450-4a9d-{identifierSuffix++:x4}-{Address:x12}");
            while (profiles.Any(profile => profile.Id == identifier));
            SavedSoundProfile migrated = new(identifier, name, _legacySoundProfile);
            migrated.Validate();
            profiles = [.. profiles, migrated];
        }
        return this with { SavedSoundProfiles = profiles, LegacySoundProfile = null };
    }

    private static string TruncateLegacyName(string name, int maximumLength)
    {
        int length = Math.Min(name.Length, maximumLength);
        if (length < name.Length && char.IsHighSurrogate(name[length - 1]))
            length--;
        return name[..length].TrimEnd();
    }

    public void Validate()
    {
        if (Address is 0 or > 0xFFFFFFFFFFFF || string.IsNullOrWhiteSpace(Name) || Name.Length > 256 || Name.Any(char.IsControl))
            throw new JsonException("Invalid saved Bluetooth device.");
        if (_legacySoundProfile is not null)
        {
            try
            {
                _legacySoundProfile.Validate();
            }
            catch (ArgumentException exception)
            {
                throw new JsonException("Invalid saved sound profile.", exception);
            }
        }
        if (SavedSoundProfiles is null || SavedSoundProfiles.Length > MaximumSoundProfiles)
            throw new JsonException("Invalid saved sound profile collection.");
        foreach (SavedSoundProfile? profile in SavedSoundProfiles)
        {
            if (profile is null)
                throw new JsonException("A saved sound profile record is missing.");
            profile.Validate();
        }
        if (SavedSoundProfiles.Select(profile => profile.Id).Distinct().Count() != SavedSoundProfiles.Length
            || SavedSoundProfiles.Select(profile => profile.Name).Distinct(StringComparer.Ordinal).Count() != SavedSoundProfiles.Length)
            throw new JsonException("Duplicate saved sound profile identifiers or names.");
    }
}

internal sealed record SavedSoundProfile(Guid Id, string Name, SoundProfile SoundProfile)
{
    internal const int MaximumNameLength = 64;

    internal static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Any(char.IsControl))
            throw new ArgumentException("A sound profile name must not contain control characters.", nameof(name));
        string normalized = name.Trim();
        if (normalized.Length is 0 or > MaximumNameLength)
            throw new ArgumentException("A sound profile name must contain between 1 and 64 characters.", nameof(name));
        return normalized;
    }

    internal void Validate()
    {
        if (Id == Guid.Empty || SoundProfile is null)
            throw new JsonException("A sound profile requires a stable identifier and sound settings.");
        try
        {
            if (!string.Equals(Name, NormalizeName(Name), StringComparison.Ordinal))
                throw new ArgumentException("A saved sound profile name must already be trimmed.", nameof(Name));
            SoundProfile.Validate();
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("Invalid saved sound profile.", exception);
        }
    }
}
