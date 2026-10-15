using Xunit;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class SimplifiedLabelTests
{
    [Theory]
    [InlineData("pl", "Input.Analog", "Analogowe")]
    [InlineData("pl", "Input.Optical", "Optyczne")]
    [InlineData("pl", "Mode.Movie3D", "Film 3D")]
    [InlineData("pl", "Mode.StereoChoice", "Stereo")]
    [InlineData("pl", "Settings.Theme", "Motyw")]
    [InlineData("pl", "Settings.TextScale", "Rozmiar tekstu")]
    [InlineData("pl", "Settings.MainScreenTab", "Ekran główny")]
    [InlineData("pl", "Settings.ResetTab", "Przywróć domyślne ustawienia")]
    [InlineData("pl", "Reset.Application", "Przywróć domyślne ustawienia")]
    [InlineData("pl", "Settings.ConfirmExit", "Ostrzegaj przed zamknięciem programu")]
    [InlineData("pl", "Exit.ConfirmTitle", "Wyjść z Yamahama")]
    [InlineData("pl", "Exit.ConfirmMessage", "Czy na pewno chcesz zamknąć Yamahama?")]
    [InlineData("en", "Input.Analog", "Analog")]
    [InlineData("en", "Input.Optical", "Optical")]
    [InlineData("en", "Mode.Movie3D", "3D Movie")]
    [InlineData("en", "Mode.StereoChoice", "Stereo")]
    [InlineData("en", "Settings.Theme", "Theme")]
    [InlineData("en", "Settings.TextScale", "Text size")]
    [InlineData("en", "Settings.MainScreenTab", "Main screen")]
    [InlineData("en", "Settings.ResetTab", "Restore default settings")]
    [InlineData("en", "Reset.Application", "Restore default settings")]
    [InlineData("en", "Exit.ConfirmTitle", "Exit Yamahama")]
    [InlineData("en", "Exit.ConfirmMessage", "Are you sure you want to close Yamahama?")]
    public void RequestedLabelsRemainConciseAndLocalized(string language, string key, string expected)
    {
        Localization.Initialize();
        Localization.SetLanguage(language);
        Assert.Equal(expected, Localization.Text(key));
    }
}
