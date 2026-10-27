using System.IO;
using Xunit;

namespace YamahaRemote.Windows.Tests;

public sealed class PrismPreferencesTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void IndependentChannelsRoundTripWithoutEnablingMutedAnnouncements(bool state, bool operations, bool important)
    {
        string path = ConfigurationTests.TestPath($"prism-preferences-{state}-{operations}-{important}.json");
        UiPreferences initial = new()
        {
            UsePrismForStateChanges = state, UsePrismForOperationMessages = operations,
            UsePrismForImportantMessages = important, AnnounceStateChanges = false,
            AnnounceExternalStateChanges = false, ShowOperationMessages = false
        };
        Assert.True(initial.TrySave(out _, path));
        UiPreferences saved = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Equal(state, saved.UsePrismForStateChanges);
        Assert.Equal(operations, saved.UsePrismForOperationMessages);
        Assert.Equal(important, saved.UsePrismForImportantMessages);
        Assert.False(saved.AnnounceStateChanges);
        Assert.False(saved.AnnounceExternalStateChanges);
        Assert.False(saved.ShowOperationMessages);
    }

    [Fact]
    public void LegacyConfigurationGetsDirectChannelsWithoutRewritingOrChangingEnableFlags()
    {
        string path = ConfigurationTests.TestPath("prism-legacy.json");
        string original = "{\"Language\":\"pl\",\"Theme\":\"Dark\",\"FirstRunCompleted\":true,\"AnnounceStateChanges\":false,\"AnnounceExternalStateChanges\":false,\"ShowOperationMessages\":false}";
        File.WriteAllText(path, original);
        UiPreferences saved = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.True(saved.UsePrismForStateChanges);
        Assert.True(saved.UsePrismForOperationMessages);
        Assert.True(saved.UsePrismForImportantMessages);
        Assert.False(saved.AnnounceStateChanges);
        Assert.False(saved.AnnounceExternalStateChanges);
        Assert.False(saved.ShowOperationMessages);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("UsePrismForStateChanges")]
    [InlineData("UsePrismForOperationMessages")]
    [InlineData("UsePrismForImportantMessages")]
    public void NonBooleanChannelsAreRejectedWithoutOverwritingConfiguration(string key)
    {
        string path = ConfigurationTests.TestPath("prism-invalid-" + key + ".json");
        string original = "{\"" + key + "\":\"true\"}";
        File.WriteAllText(path, original);
        UiPreferences.Load(out string? warning, path);
        Assert.NotNull(warning);
        Assert.Equal(original, File.ReadAllText(path));
    }
}
