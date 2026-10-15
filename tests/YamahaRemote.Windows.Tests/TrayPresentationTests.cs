using System.Globalization;
using System.Text;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class TrayPresentationTests
{
    private static SoundbarState ConfirmedState => new(true, SoundbarInput.Tv, 25, false, -2,
        SoundMode.Standard, false, false, IndicatorBrightness.Bright, DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData("pl")]
    [InlineData("en")]
    public void RealLocalizationFormatsStartupAndConfirmedStatesWithoutDoubleFormatting(string language)
    {
        string previousLanguage = Localization.CurrentLanguage;
        Localization.Initialize();
        try
        {
            Localization.SetLanguage(language);
            SoundbarState?[] states = [null, ConfirmedState, ConfirmedState with { Power = false }];
            foreach (bool connected in new[] { false, true })
            foreach (bool busy in new[] { false, true })
            foreach (SoundbarState? state in states)
            {
                TrayPresentation result = TrayPresentation.Create(connected, state, busy,
                    Localization.Text, Localization.Culture);

                Dictionary<string, string> texts = Texts(language);
                texts["Tray.Subwoofer"] = language == "pl" ? "Wzmocnienie subwoofera" : "Subwoofer gain";
                texts["Tray.Summary"] = language == "pl"
                    ? "Yamahama — {0}; zasilanie {1}; głośność {2}; wzmocnienie subwoofera {3}"
                    : "Yamahama — {0}; power {1}; volume {2}; subwoofer gain {3}";
                Assert.Equal(Create(language, connected, state, busy, texts), result);
                Assert.DoesNotContain("{0}", result.Tooltip);
                Assert.InRange(result.Tooltip.Length, 1, TrayPresentation.TooltipLimit);
            }
        }
        finally
        {
            Localization.SetLanguage(previousLanguage);
        }
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("en")]
    public void ProviderFormatsEscapedBracesOnlyOnce(string language)
    {
        Dictionary<string, string> texts = Texts(language);
        texts["Tray.Summary"] += " {{summary}}";
        texts["Tray.GroupValue"] = "{{group}} {0}: {1}";
        TrayPresentation result = Create(language, true, ConfirmedState, texts: texts);

        Assert.Contains("{summary}", result.Tooltip);
        Assert.Equal("{group} " + texts["Tray.Volume"] + ": 25", result.VolumeLabel);
        Assert.Equal("{group} Subwoofer: -2", result.SubwooferLabel);
    }

    [Theory]
    [InlineData("pl", "Yamahama — SR-C30A: Połączono — stan potwierdzony; zasilanie Włączone; głośność 25; subwoofer -2; Źródło dźwięku: TV",
        "Głośność: 25", "Źródło dźwięku: TV")]
    [InlineData("en", "Yamahama — SR-C30A: Connected — confirmed state; power On; volume 25; subwoofer -2; Audio source: TV",
        "Volume: 25", "Audio source: TV")]
    public void ConfirmedStateFormatsModelAndLocalizedValues(string language, string tooltip, string volume, string input)
    {
        TrayPresentation result = Create(language, true, ConfirmedState);

        Assert.Equal(tooltip, result.Tooltip);
        Assert.Equal(volume, result.VolumeLabel);
        Assert.Equal("Subwoofer: -2", result.SubwooferLabel);
        Assert.Equal(input, result.InputLabel);
        Assert.Equal(Texts(language)["Tray.Connected"], result.Status);
        Assert.InRange(result.Tooltip.Length, 1, TrayPresentation.TooltipLimit);
    }

    [Theory]
    [InlineData("pl", "zasilanie Wyłączone")]
    [InlineData("en", "power Off")]
    public void PowerOffRemainsConfirmedNotDisconnected(string language, string power)
    {
        TrayPresentation result = Create(language, true, ConfirmedState with { Power = false });

        Assert.Contains(power, result.Tooltip);
        Assert.Equal(Texts(language)["Tray.Connected"], result.Status);
        Assert.Equal(Create(language, true, ConfirmedState).VolumeLabel, result.VolumeLabel);
    }

    [Theory]
    [InlineData("pl", false)]
    [InlineData("pl", true)]
    [InlineData("en", false)]
    [InlineData("en", true)]
    public void DisconnectedStateCannotExposeRetainedValues(string language, bool busy)
    {
        TrayPresentation result = Create(language, false, ConfirmedState, busy);

        Assert.Equal(Create(language, false, null, busy), result);
        Assert.Contains(Texts(language)["Tray.NotConnected"], result.Status);
        Assert.Contains(Texts(language)["Tray.NotConnected"], result.Tooltip);
        AssertUnknownValues(language, result);
    }

    [Theory]
    [InlineData("pl", false)]
    [InlineData("pl", true)]
    [InlineData("en", false)]
    [InlineData("en", true)]
    public void ConnectedWithoutCurrentStateShowsUnknownValues(string language, bool busy)
    {
        TrayPresentation result = Create(language, true, null, busy);

        Assert.Contains(Texts(language)["Tray.StateUnknown"], result.Status);
        Assert.Contains(Texts(language)["Tray.StateUnknown"], result.Tooltip);
        AssertUnknownValues(language, result);
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("en")]
    public void BusyStateKeepsConfirmedValuesAndConnection(string language)
    {
        Dictionary<string, string> texts = Texts(language);
        TrayPresentation idle = Create(language, true, ConfirmedState);
        TrayPresentation busy = Create(language, true, ConfirmedState, true);

        Assert.Equal(idle.VolumeLabel, busy.VolumeLabel);
        Assert.Equal(idle.SubwooferLabel, busy.SubwooferLabel);
        Assert.Equal(idle.InputLabel, busy.InputLabel);
        Assert.Contains(texts["State.On"], busy.Tooltip);
        Assert.Contains(texts["Tray.Connected"], busy.Tooltip);
        Assert.Contains("25; subwoofer -2", busy.Tooltip);
        Assert.Contains(texts["Tray.Busy"], busy.Tooltip);
        Assert.Equal(texts["Tray.Connected"] + "; " + texts["Tray.Busy"], busy.Status);
        Assert.InRange(busy.Tooltip.Length, 1, TrayPresentation.TooltipLimit);
    }

    [Theory]
    [InlineData("pl", 0, -4, true, true)]
    [InlineData("pl", 50, 4, true, true)]
    [InlineData("pl", -1, -5, false, false)]
    [InlineData("pl", 51, 5, false, false)]
    [InlineData("pl", 255, 255, false, false)]
    [InlineData("pl", int.MinValue, int.MaxValue, false, false)]
    [InlineData("pl", 25, 5, true, false)]
    [InlineData("pl", -1, -2, false, true)]
    [InlineData("en", 0, -4, true, true)]
    [InlineData("en", 50, 4, true, true)]
    [InlineData("en", -1, -5, false, false)]
    [InlineData("en", 51, 5, false, false)]
    [InlineData("en", 255, 255, false, false)]
    [InlineData("en", int.MinValue, int.MaxValue, false, false)]
    [InlineData("en", 25, 5, true, false)]
    [InlineData("en", -1, -2, false, true)]
    public void LevelBoundsAreValidatedIndependently(string language, int volume, int subwoofer,
        bool validVolume, bool validSubwoofer)
    {
        Dictionary<string, string> texts = Texts(language);
        string expectedVolume = validVolume ? volume.ToString(CultureInfo.InvariantCulture) : texts["Readout.UnknownFeminine"];
        string expectedSubwoofer = validSubwoofer ? subwoofer.ToString(CultureInfo.InvariantCulture) : texts["Readout.Unknown"];
        TrayPresentation result = Create(language, true, ConfirmedState with { Volume = volume, Subwoofer = subwoofer });

        Assert.Equal(texts["Tray.Volume"] + ": " + expectedVolume, result.VolumeLabel);
        Assert.Equal(texts["Tray.Subwoofer"] + ": " + expectedSubwoofer, result.SubwooferLabel);
        Assert.Contains(expectedVolume + "; subwoofer " + expectedSubwoofer, result.Tooltip);
        Assert.Contains(texts["State.On"], result.Tooltip);
    }

    [Theory]
    [InlineData("pl", SoundbarInput.Tv, "TV", true)]
    [InlineData("pl", SoundbarInput.Bluetooth, "Bluetooth", true)]
    [InlineData("pl", SoundbarInput.Optical, "Optyczne (OPTICAL)", false)]
    [InlineData("pl", SoundbarInput.Analog, "Analogowe (ANALOG)", false)]
    [InlineData("en", SoundbarInput.Tv, "TV", true)]
    [InlineData("en", SoundbarInput.Bluetooth, "Bluetooth", true)]
    [InlineData("en", SoundbarInput.Optical, "Optical (OPTICAL)", true)]
    [InlineData("en", SoundbarInput.Analog, "Analog (ANALOG)", true)]
    public void KnownInputsAreLocalizedAndOptionalInTooltip(string language, SoundbarInput input,
        string expected, bool fits)
    {
        TrayPresentation result = Create(language, true, ConfirmedState with { Input = input });

        Assert.Equal(Texts(language)["Tray.Inputs"] + ": " + expected, result.InputLabel);
        Assert.Equal(fits, result.Tooltip.Contains(result.InputLabel, StringComparison.Ordinal));
        Assert.Contains("25; subwoofer -2", result.Tooltip);
    }

    [Theory]
    [InlineData("pl", "Źródło dźwięku: Nieznane wejście")]
    [InlineData("en", "Audio source: Unknown input")]
    public void UnknownInputDoesNotPretendToBeAKnownSource(string language, string expected)
    {
        TrayPresentation result = Create(language, true, ConfirmedState with { Input = (SoundbarInput)255 });

        Assert.Equal(expected, result.InputLabel);
        Assert.DoesNotContain("255", result.Tooltip);
        Assert.Equal(Create(language, true, ConfirmedState).VolumeLabel, result.VolumeLabel);
    }

    [Theory]
    [InlineData("pl", 126)]
    [InlineData("pl", 127)]
    [InlineData("pl", 128)]
    [InlineData("en", 126)]
    [InlineData("en", 127)]
    [InlineData("en", 128)]
    public void OptionalInputMustFitInFull(string language, int combinedLength)
    {
        Dictionary<string, string> texts = Texts(language);
        TrayPresentation baseline = Create(language, true, ConfirmedState);
        texts["Input.Tv"] = new string('x', combinedLength - baseline.Tooltip.Length + texts["Input.Tv"].Length);
        TrayPresentation result = Create(language, true, ConfirmedState, texts: texts);

        if (combinedLength <= TrayPresentation.TooltipLimit)
        {
            Assert.Equal(combinedLength, result.Tooltip.Length);
            Assert.EndsWith("; " + result.InputLabel, result.Tooltip);
        }
        else
        {
            Assert.Equal(baseline.Tooltip[..^(baseline.InputLabel.Length + 2)], result.Tooltip);
            Assert.DoesNotContain(texts["Tray.Inputs"], result.Tooltip);
        }
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("en")]
    public void LongTranslationsAreBoundedWithoutChangingFullMenuLabels(string language)
    {
        Dictionary<string, string> texts = Texts(language);
        texts["Tray.Connected"] += new string('ą', 200);
        texts["Tray.Volume"] = "_" + texts["Tray.Volume"];
        TrayPresentation result = Create(language, true, ConfirmedState, true, texts);

        Assert.Equal(TrayPresentation.TooltipLimit, result.Tooltip.Length);
        Assert.EndsWith("…", result.Tooltip);
        Assert.StartsWith("Yamahama — SR-C30A: ", result.Tooltip);
        Assert.Contains(texts["Tray.Connected"], result.Status);
        Assert.Equal(Create(language, true, ConfirmedState).VolumeLabel, result.VolumeLabel);
        Assert.Equal(Create(language, true, ConfirmedState).InputLabel, result.InputLabel);
    }

    [Theory]
    [InlineData("pl", 'ą')]
    [InlineData("en", 'a')]
    public void TooltipLimitPreservesShortStringsAndTruncatesLongStrings(string language, char character)
    {
        Assert.NotEmpty(Texts(language));
        foreach (int length in new[] { 0, 1, 126, 127, 128, 1000 })
        {
            string text = new(character, length);
            string expected = length <= TrayPresentation.TooltipLimit ? text : text[..126] + "…";
            Assert.Equal(expected, TrayPresentation.LimitTooltip(text));
        }
    }

    [Theory]
    [InlineData("pl", 124, 127)]
    [InlineData("pl", 125, 126)]
    [InlineData("pl", 126, 127)]
    [InlineData("en", 124, 127)]
    [InlineData("en", 125, 126)]
    [InlineData("en", 126, 127)]
    public void TruncationDoesNotSplitUtf16SurrogatePairs(string language, int prefixLength, int expectedLength)
    {
        string prefix = new(language == "pl" ? 'ą' : 'a', prefixLength);
        string text = prefix + "\U0001F3B5" + " suffix";
        string result = TrayPresentation.LimitTooltip(text);

        Assert.Equal(expectedLength, result.Length);
        Assert.EndsWith("…", result);
        Assert.NotEmpty(new UnicodeEncoding(false, false, true).GetBytes(result));
        string exactLimit = new string('a', 125) + "\U0001F3B5";
        Assert.Equal(exactLimit, TrayPresentation.LimitTooltip(exactLimit));
    }

    private static void AssertUnknownValues(string language, TrayPresentation result)
    {
        Dictionary<string, string> texts = Texts(language);
        Assert.Equal(texts["Tray.Volume"] + ": " + texts["Readout.UnknownFeminine"], result.VolumeLabel);
        Assert.Equal(texts["Tray.Subwoofer"] + ": " + texts["Readout.Unknown"], result.SubwooferLabel);
        Assert.Equal(texts["Tray.Inputs"] + ": " + texts["Readout.Unknown"], result.InputLabel);
        Assert.Contains(texts["Readout.Unknown"], result.Tooltip);
        Assert.DoesNotContain(texts["State.On"], result.Tooltip);
        Assert.DoesNotContain(texts["Feedback.PowerOff"], result.Tooltip);
        Assert.DoesNotContain("25", result.Tooltip);
        Assert.DoesNotContain("-2", result.Tooltip);
        Assert.DoesNotContain("TV", result.Tooltip);
        Assert.InRange(result.Tooltip.Length, 1, TrayPresentation.TooltipLimit);
    }

    private static TrayPresentation Create(string language, bool connected, SoundbarState? state, bool busy = false,
        Dictionary<string, string>? texts = null)
    {
        Dictionary<string, string> catalogue = texts ?? Texts(language);
        CultureInfo culture = CultureInfo.GetCultureInfo(language);
        return TrayPresentation.Create(connected, state, busy,
            (key, arguments) => string.Format(culture, catalogue[key], arguments), culture);
    }

    private static Dictionary<string, string> Texts(string language)
    {
        bool polish = language == "pl";
        return new(StringComparer.Ordinal)
        {
            ["Tray.Summary"] = polish ? "Yamahama — {0}; zasilanie {1}; głośność {2}; subwoofer {3}"
                : "Yamahama — {0}; power {1}; volume {2}; subwoofer {3}",
            ["Tray.GroupValue"] = "{0}: {1}",
            ["Tray.NotConnected"] = polish ? "Brak połączenia" : "Not connected",
            ["Tray.Connected"] = polish ? "Połączono — stan potwierdzony" : "Connected — confirmed state",
            ["Tray.StateUnknown"] = polish ? "Połączono — stan niepotwierdzony" : "Connected — unconfirmed state",
            ["Tray.Busy"] = polish ? "Operacja w toku" : "Operation in progress",
            ["Tray.Volume"] = polish ? "Głośność" : "Volume",
            ["Tray.Subwoofer"] = "Subwoofer",
            ["Tray.Inputs"] = polish ? "Źródło dźwięku" : "Audio source",
            ["Readout.Unknown"] = polish ? "Nieznane" : "Unknown",
            ["Readout.UnknownFeminine"] = polish ? "Nieznana" : "Unknown",
            ["Readout.UnknownMasculine"] = polish ? "Nieznany" : "Unknown",
            ["State.On"] = polish ? "Włączone" : "On",
            ["Feedback.PowerOff"] = polish ? "Wyłączone" : "Off",
            ["Input.Tv"] = "TV",
            ["Input.Bluetooth"] = "Bluetooth",
            ["Input.Optical"] = polish ? "Optyczne (OPTICAL)" : "Optical (OPTICAL)",
            ["Input.Analog"] = polish ? "Analogowe (ANALOG)" : "Analog (ANALOG)",
            ["Input.Unknown"] = polish ? "Nieznane wejście" : "Unknown input"
        };
    }
}
