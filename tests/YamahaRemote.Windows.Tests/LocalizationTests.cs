using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void BothCataloguesResolveEveryLiteralUiAndOperationKey()
    {
        HashSet<string> keys = [];
        foreach (string file in Directory.EnumerateFiles(Path.Combine(TestPaths.ProjectRoot, "src", "YamahaRemote.Windows")))
        {
            if (Path.GetExtension(file) is not (".cs" or ".xaml"))
                continue;
            string source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, "(?:Localization.Text|LocalizedOperationException)\\(\"([A-Za-z][A-Za-z0-9.]+)\"|\\{DynamicResource ([A-Za-z][A-Za-z0-9.]+)\\}"))
                keys.Add(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
            foreach (Match match in Regex.Matches(source, "\"((?:Tray|Command|Control|Connection)\\.[A-Za-z0-9.]+)\""))
                keys.Add(match.Groups[1].Value);
        }
        Assert.True(keys.Count > 300);
        foreach (string language in new[] { "pl", "en" })
        {
            using JsonDocument catalogue = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "langs", language + ".lng")));
            foreach (string key in keys)
            {
                Assert.True(catalogue.RootElement.TryGetProperty(key, out JsonElement value), $"{language}: {key}");
                Assert.False(string.IsNullOrWhiteSpace(value.GetString()));
            }
        }
    }

    [Theory]
    [InlineData("pl", "Jasny")]
    [InlineData("en", "Light")]
    public void LanguageChangesIncludeDynamicErrorsAndFormats(string language, string expected)
    {
        Localization.Initialize();
        Localization.SetLanguage(language);
        Assert.Equal(expected, Localization.Text("Theme.Light"));
        Assert.Contains("sample", Localization.Text("Device.SavedEntry", "sample", "address"));
        Assert.DoesNotContain("{0}", Localization.Text("Device.SavedEntry", "sample", "address"));
        Assert.Equal(Localization.Text("Error.Timeout"), MainWindow.DescribeError(new TimeoutException()));
        Assert.DoesNotContain("Untranslated injected message", MainWindow.DescribeError(new InvalidOperationException("Untranslated injected message")));
    }

    [Theory]
    [InlineData("{}", "{}")]
    [InlineData("{\"Key\":\"value\"}", "{\"Other\":\"value\"}")]
    [InlineData("{\"Key\":\"{0}\"}", "{\"Key\":\"{1}\"}")]
    [InlineData("{\"Key\":\"value\",\"Key\":\"duplicate\"}", "{\"Key\":\"value\"}")]
    [InlineData("{\"Key\":3}", "{\"Key\":\"value\"}")]
    [InlineData("{\"Key\":\"\"}", "{\"Key\":\"value\"}")]
    public void InvalidLanguageResourcesAreRejectedAtomically(string polish, string english)
    {
        Localization.Initialize();
        Localization.SetLanguage("en");
        string directory = Path.GetDirectoryName(ConfigurationTests.TestPath(Path.Combine("invalid-langs", "pl.lng")))!;
        File.WriteAllText(Path.Combine(directory, "pl.lng"), polish);
        File.WriteAllText(Path.Combine(directory, "en.lng"), english);
        Assert.Throws<InvalidDataException>(() => Localization.Initialize(directory));
        Assert.Equal("Light", Localization.Text("Theme.Light"));
    }

    [Fact]
    public void UnsupportedLanguageDoesNotChangeSelection()
    {
        Localization.Initialize();
        Localization.SetLanguage("en");
        Assert.Throws<ArgumentOutOfRangeException>(() => Localization.SetLanguage("de"));
        Assert.Equal("en", Localization.CurrentLanguage);
    }
}
