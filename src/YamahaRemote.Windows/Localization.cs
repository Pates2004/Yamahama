using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace YamahaRemote.Windows;

public static class Localization
{
    private static readonly object SyncRoot = new();
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? _catalogues;
    private static string _currentLanguage = "pl";

    public static event EventHandler? LanguageChanged;

    public static string CurrentLanguage
    {
        get
        {
            lock (SyncRoot)
                return _currentLanguage;
        }
    }

    public static CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLanguage == "en" ? "en-GB" : "pl-PL");

    public static void Initialize(string? directory = null)
    {
        string location = directory ?? Path.Combine(AppContext.BaseDirectory, "langs");
        Dictionary<string, IReadOnlyDictionary<string, string>> catalogues = new(StringComparer.Ordinal);
        foreach (string language in new[] { "pl", "en" })
        {
            string path = Path.Combine(location, language + ".lng");
            using StreamReader reader = new(path, new UTF8Encoding(false, true));
            using JsonDocument document = JsonDocument.Parse(reader.ReadToEnd());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Language file must contain a JSON string dictionary: {path}");
            Dictionary<string, string> entries = new(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name) || property.Value.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(property.Value.GetString())
                    || !entries.TryAdd(property.Name, property.Value.GetString()!))
                    throw new InvalidDataException($"Invalid or duplicate language entry: {path}, {property.Name}");
                _ = CompositeFormat.Parse(entries[property.Name]);
            }
            if (entries.Count == 0)
                throw new InvalidDataException($"Language file is empty: {path}");
            catalogues.Add(language, entries);
        }
        if (!catalogues["pl"].Keys.Order(StringComparer.Ordinal).SequenceEqual(catalogues["en"].Keys.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Polish and English language files must contain the same keys.");
        foreach (string key in catalogues["pl"].Keys)
        {
            if (CompositeFormat.Parse(catalogues["pl"][key]).MinimumArgumentCount
                != CompositeFormat.Parse(catalogues["en"][key]).MinimumArgumentCount)
                throw new InvalidDataException($"Language format arguments do not match: {key}");
        }
        lock (SyncRoot)
            _catalogues = catalogues;
        SetLanguage(CurrentLanguage);
    }

    public static void SetLanguage(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        string normalized = language.ToLowerInvariant();
        if (normalized is not ("pl" or "en"))
            throw new ArgumentOutOfRangeException(nameof(language), language, "Supported languages: pl, en.");
        EnsureInitialized();
        bool changed;
        IReadOnlyDictionary<string, string> entries;
        lock (SyncRoot)
        {
            changed = _currentLanguage != normalized;
            _currentLanguage = normalized;
            entries = _catalogues![normalized];
        }
        Application? application = Application.Current;
        if (application is null || application.Dispatcher.HasShutdownStarted || application.Dispatcher.HasShutdownFinished)
            return;
        void ApplyLanguage()
        {
            if (application.Dispatcher.HasShutdownStarted || application.Dispatcher.HasShutdownFinished)
                return;
            foreach (KeyValuePair<string, string> entry in entries)
                application.Resources[entry.Key] = entry.Value;
            if (changed)
                LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
        if (application.Dispatcher.CheckAccess())
            ApplyLanguage();
        else
        {
            try
            {
                application.Dispatcher.Invoke(ApplyLanguage);
            }
            catch (TaskCanceledException) when (application.Dispatcher.HasShutdownStarted || application.Dispatcher.HasShutdownFinished)
            {
            }
            catch (InvalidOperationException) when (application.Dispatcher.HasShutdownStarted || application.Dispatcher.HasShutdownFinished)
            {
            }
        }
    }

    public static string Text(string key, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(args);
        EnsureInitialized();
        string template;
        CultureInfo culture;
        lock (SyncRoot)
        {
            if (!_catalogues![_currentLanguage].TryGetValue(key, out template!))
                throw new KeyNotFoundException($"Missing localization key: {key}");
            culture = CultureInfo.GetCultureInfo(_currentLanguage == "en" ? "en-GB" : "pl-PL");
        }
        return string.Format(culture, template, args);
    }

    private static void EnsureInitialized()
    {
        lock (SyncRoot)
        {
            if (_catalogues is not null)
                return;
        }
        Initialize();
    }
}

internal sealed class LocalizedOperationException(string key, params object[] arguments)
    : InvalidOperationException(Localization.Text(key, arguments))
{
    public override string Message => Localization.Text(key, arguments);
}
