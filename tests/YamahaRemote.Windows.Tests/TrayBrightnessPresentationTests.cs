using System.Globalization;
using System.IO;
using System.Text.Json;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class TrayBrightnessPresentationTests
{
    [Theory]
    [InlineData("pl", IndicatorBrightness.Bright, "Jasność wskaźników: Jasne")]
    [InlineData("pl", IndicatorBrightness.Dim, "Jasność wskaźników: Przyciemnione")]
    [InlineData("pl", IndicatorBrightness.Off, "Jasność wskaźników: Wyłączone")]
    [InlineData("en", IndicatorBrightness.Bright, "Indicator brightness: Bright")]
    [InlineData("en", IndicatorBrightness.Dim, "Indicator brightness: Dimmed")]
    [InlineData("en", IndicatorBrightness.Off, "Indicator brightness: Off")]
    public void BrightnessUsesConfirmedLocalizedValue(string language, IndicatorBrightness brightness, string expected)
    {
        Dictionary<string, string> texts = LoadTexts(language);
        SoundbarState state = State(brightness);

        Assert.Equal(expected, Format(true, state, language, texts));
        Assert.Equal(expected, Format(true, state with { Power = false }, language, texts));
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("en")]
    public void DisconnectedOrMissingStateNeverDisplaysStaleBrightness(string language)
    {
        Dictionary<string, string> texts = LoadTexts(language);
        string expected = texts["Tray.Brightness"] + ": " + texts["Readout.UnknownFeminine"];

        Assert.Equal(expected, Format(false, State(IndicatorBrightness.Dim), language, texts));
        Assert.Equal(expected, Format(false, null, language, texts));
        Assert.Equal(expected, Format(true, null, language, texts));
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("en")]
    public void InvalidBrightnessIsUnknownAndGroupFormatIsAppliedOnce(string language)
    {
        Dictionary<string, string> texts = LoadTexts(language);
        texts["Tray.GroupValue"] = "{{group}} {0}: {1}";
        texts["Tray.Brightness"] = "_" + texts["Tray.Brightness"];
        SoundbarState state = State((IndicatorBrightness)255);

        string result = Format(true, state, language, texts);

        Assert.Equal("{group} " + texts["Tray.Brightness"][1..] + ": " + texts["Brightness.Unknown"], result);
        Assert.DoesNotContain("255", result);
    }

    private static SoundbarState State(IndicatorBrightness brightness) => new(true, SoundbarInput.Tv, 20, false, 0,
        SoundMode.Standard, false, false, brightness, DateTimeOffset.UnixEpoch);

    private static string Format(bool connected, SoundbarState? state, string language, Dictionary<string, string> texts) =>
        TrayPresentation.FormatBrightnessLabel(connected, state,
            (key, arguments) => string.Format(CultureInfo.GetCultureInfo(language), texts[key], arguments));

    private static Dictionary<string, string> LoadTexts(string language) => JsonSerializer.Deserialize<Dictionary<string, string>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "langs", language + ".lng")))!;
}
