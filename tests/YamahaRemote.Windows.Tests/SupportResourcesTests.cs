using Xunit;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class SupportResourcesTests
{
    [Theory]
    [InlineData("pl", SupportGuide.Firmware, "UPDATE ONLY", "Nie oznacza to, że 1.00 jest najnowszą")]
    [InlineData("pl", SupportGuide.TouchPanel, "INFO", "co najmniej 5 sekund")]
    [InlineData("en", SupportGuide.Firmware, "UPDATE ONLY", "does not mean that 1.00 is the latest")]
    [InlineData("en", SupportGuide.TouchPanel, "INFO", "at least 5 seconds")]
    [InlineData("pl", SupportGuide.Reset, "9034212747.html", "BLE")]
    [InlineData("en", SupportGuide.Reset, "9034212747.html", "BLE")]
    public void EmbeddedGuidesContainSafetyGuidance(string language, SupportGuide guide, string expectedRule, string expectedDetail)
    {
        Localization.SetLanguage(language);
        string text = SupportResources.ReadGuide(guide);
        Assert.StartsWith("Yamahama", text);
        Assert.Contains(expectedRule, text);
        Assert.Contains(expectedDetail, text);
        Assert.DoesNotContain("\uFFFD", text);
    }

    [Theory]
    [InlineData(SupportResources.FirmwareDownloadsAddress, "europe.yamaha.com", "/sr-c30a/support.html")]
    [InlineData(SupportResources.YamahaSupportAddress, "pl.yamaha.com", "/support/contacts/index.html")]
    public void SupportAddressesUseTheVerifiedOfficialPages(string address, string host, string pathSuffix)
    {
        Localization.SetLanguage("pl");
        Uri page = new(address);
        Assert.Equal("https", page.Scheme);
        Assert.Equal(host, page.Host);
        Assert.EndsWith(pathSuffix, page.AbsolutePath);
        Assert.Contains(address, SupportResources.ReadGuide(SupportGuide.Firmware));
    }

    [Fact]
    public void OnlyApplicationSupportGuidesAreEmbedded()
    {
        string[] expected =
        [
            "Yamahama.Guides.Firmware.en.txt",
            "Yamahama.Guides.Firmware.pl.txt",
            "Yamahama.Guides.Reset.en.txt",
            "Yamahama.Guides.Reset.pl.txt",
            "Yamahama.Guides.TouchPanel.en.txt",
            "Yamahama.Guides.TouchPanel.pl.txt",
            "Yamahama.g.resources"
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal),
            typeof(SupportResources).Assembly.GetManifestResourceNames().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void UnknownGuideIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SupportResources.ReadGuide((SupportGuide)99));

    [Fact]
    public void PublishedAssemblyHasTheRequestedName() =>
        Assert.Equal("Yamahama", typeof(MainWindow).Assembly.GetName().Name);
}
