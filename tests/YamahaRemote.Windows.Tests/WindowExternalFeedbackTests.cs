using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static async Task ExerciseExternalFeedbackAsync()
    {
        foreach (string language in new[] { "pl", "en" })
        {
            Localization.SetLanguage(language);
            string path = ConfigurationTests.TestPath("external-feedback-" + language + ".json");
            UiPreferences preferences = new()
            {
                ConfirmExit = false, FirstRunCompleted = true, Theme = "Dark", Language = language,
                ShowHints = false, ShowOperationMessages = false, AnnounceStateChanges = false,
                AnnounceExternalStateChanges = true, CloseToTray = true
            };
            Assert.True(preferences.TrySave(out _, path));
            List<string> announcements = [];
            FakeSoundbarController controller = new();
            MainWindow window = new(controller, null, path, new FakeStartupRegistration(), (_, _) => true, announceState: announcements.Add);
            window.Show();
            await SettleReadoutWindowAsync(window);
            T Control<T>(string name) where T : FrameworkElement => Assert.IsType<T>(window.FindName(name));
            async Task ClickAsync(string name)
            {
                Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            }
            async Task PublishAsync(SoundbarState state)
            {
                controller.PublishState(state);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
            await ClickAsync("SearchButton");
            Control<ListBox>("DevicesList").SelectedIndex = 0;
            await ClickAsync("ConnectButton");
            Assert.Empty(announcements);
            Assert.True(Control<ConfirmedSlider>("VolumeSlider").Focus());
            await PublishAsync(controller.State! with { Volume = 9 });
            Assert.Equal(Localization.Text("Feedback.Volume", "9"), Assert.Single(announcements));
            await PublishAsync(controller.State!);
            Assert.Single(announcements);
            Assert.True(window.HideForCloseShortcut());
            await PublishAsync(controller.State! with
            {
                Input = SoundbarInput.Optical, Muted = true, Subwoofer = 2, Mode = SoundMode.Game,
                ClearVoice = true, BassExtension = true, Brightness = IndicatorBrightness.Dim
            });
            string message = announcements[^1];
            foreach (string key in new[] { "Input", "Mute", "Subwoofer", "SoundMode", "ClearVoice", "BassExtension", "IndicatorBrightness" })
            {
                string template = Localization.Text("Feedback." + key, "VALUE");
                Assert.Contains(template.Split("VALUE")[0], message);
            }
            Assert.Equal(2, announcements.Count);
            await PublishAsync(controller.State! with { Power = false });
            Assert.Equal(Localization.Text("Feedback.Power", Localization.Text("Feedback.PowerOff")), announcements[^1]);
            window.RestoreFromTray();
            Control<CheckBox>("AnnounceExternalStateChangesCheckBox").IsChecked = false;
            await PublishAsync(controller.State! with { Power = true });
            Assert.Equal(4, announcements.Count);
            Assert.True(window.CancelSettings());
            Assert.True(Control<CheckBox>("AnnounceExternalStateChangesCheckBox").IsChecked);
            Control<CheckBox>("AnnounceExternalStateChangesCheckBox").IsChecked = false;
            Assert.True(window.CommitSettings());
            await PublishAsync(controller.State! with { Volume = 10 });
            Assert.Equal(4, announcements.Count);
            Control<CheckBox>("AnnounceExternalStateChangesCheckBox").IsChecked = true;
            Assert.True(window.CommitSettings());
            await PublishAsync(controller.State!);
            Assert.Equal(4, announcements.Count);
            await PublishAsync(controller.State! with { Volume = 11 });
            Assert.Equal(Localization.Text("Feedback.Volume", "11"), announcements[^1]);
            Assert.Equal(5, announcements.Count);
            await window.ExecuteGlobalHotkeyAsync(HotkeyAction.ToggleMute);
            Assert.Equal(5, announcements.Count);
            controller.DropConnection();
            await SettleReadoutWindowAsync(window);
            Assert.Equal(5, announcements.Count);
            Assert.True(UiPreferences.Load(out _, path).AnnounceExternalStateChanges);
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }
}
