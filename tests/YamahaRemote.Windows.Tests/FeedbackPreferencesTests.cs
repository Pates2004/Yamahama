using System.IO;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class FeedbackPreferencesTests
{
    private static readonly SoundbarState Initial = new(true, SoundbarInput.Tv, 10, false, 0,
        SoundMode.Stereo, false, false, IndicatorBrightness.Bright, DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void IndependentFeedbackChoicesRoundTrip(bool operations, bool fields, bool announcements)
    {
        string path = ConfigurationTests.TestPath($"feedback-preferences-{operations}-{fields}-{announcements}.json");
        UiPreferences original = new()
        {
            ShowOperationMessages = operations, UseReadOnlyValueFields = fields, AnnounceStateChanges = announcements
        };
        Assert.True(original.TrySave(out _, path));
        UiPreferences saved = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Equal(operations, saved.ShowOperationMessages);
        Assert.Equal(fields, saved.UseReadOnlyValueFields);
        Assert.Equal(fields ? ValueDisplayMode.ReadOnlyFields : ValueDisplayMode.StaticText, saved.ValueDisplayMode);
        Assert.Equal(announcements, saved.AnnounceStateChanges);
        Assert.True(saved.ShowHints);
    }

    [Fact]
    public void ExistingConfigurationUsesQuietStaticDefaultsWithoutLosingOtherSettings()
    {
        string path = ConfigurationTests.TestPath("feedback-old-config.json");
        File.WriteAllText(path, "{\"SchemaVersion\":1,\"Language\":\"en\",\"Theme\":\"Dark\",\"ShowHints\":false,\"FirstRunCompleted\":true}");
        UiPreferences saved = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.True(saved.IsSetupComplete);
        Assert.False(saved.ShowOperationMessages);
        Assert.False(saved.UseReadOnlyValueFields);
        Assert.Equal(ValueDisplayMode.StaticText, saved.ValueDisplayMode);
        Assert.True(saved.AnnounceStateChanges);
        Assert.False(saved.ShowHints);
        Assert.Equal("Dark", saved.Theme);
    }

    [Theory]
    [InlineData("ShowOperationMessages")]
    [InlineData("UseReadOnlyValueFields")]
    [InlineData("AnnounceStateChanges")]
    public void NonBooleanPreferencesAreRejectedWithoutOverwritingTheFile(string key)
    {
        Localization.Initialize();
        string path = ConfigurationTests.TestPath("invalid-" + key + ".json");
        string invalid = "{\"" + key + "\":\"true\"}";
        File.WriteAllText(path, invalid);
        UiPreferences.Load(out string? warning, path);
        Assert.NotNull(warning);
        Assert.Equal(invalid, File.ReadAllText(path));
    }

    [Fact]
    public void IdenticalPollingOnlyChangesTimestampAndProducesNoFeedback()
    {
        Assert.Empty(SoundbarFeedback.ChangedRows(Initial, Initial));
        Assert.Empty(SoundbarFeedback.ChangedRows(Initial, Initial with { ReceivedAt = Initial.ReceivedAt.AddSeconds(10) }));
    }

    [Theory]
    [InlineData("Power")]
    [InlineData("Input")]
    [InlineData("Volume")]
    [InlineData("Mute")]
    [InlineData("Subwoofer")]
    [InlineData("SoundMode")]
    [InlineData("ClearVoice")]
    [InlineData("BassExtension")]
    [InlineData("IndicatorBrightness")]
    public void EachConfirmedFieldProducesOnlyItsOwnChange(string name)
    {
        ControlRow row = Enum.Parse<ControlRow>(name);
        SoundbarState changed = row switch
        {
            ControlRow.Power => Initial with { Power = false },
            ControlRow.Input => Initial with { Input = SoundbarInput.Bluetooth },
            ControlRow.Volume => Initial with { Volume = 9 },
            ControlRow.Mute => Initial with { Muted = true },
            ControlRow.Subwoofer => Initial with { Subwoofer = -1 },
            ControlRow.SoundMode => Initial with { Mode = SoundMode.Game },
            ControlRow.ClearVoice => Initial with { ClearVoice = true },
            ControlRow.BassExtension => Initial with { BassExtension = true },
            ControlRow.IndicatorBrightness => Initial with { Brightness = IndicatorBrightness.Off },
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(new[] { row }, SoundbarFeedback.ChangedRows(Initial, changed));
        Assert.Equal(new[] { row }, SoundbarFeedback.ChangedRows(changed, Initial));
        foreach (string language in new[] { "pl", "en" })
        {
            Localization.Initialize();
            Localization.SetLanguage(language);
            string text = Localization.Text($"Feedback.{row}", "sample");
            Assert.Contains("sample", text);
            Assert.DoesNotContain("{0}", text);
        }
    }
}
