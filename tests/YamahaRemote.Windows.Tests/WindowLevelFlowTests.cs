using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static async Task ExerciseLevelsAsync()
    {
        foreach (string language in new[] { "pl", "en" })
        {
            string path = ConfigurationTests.TestPath("levels-" + language + ".json");
            UiPreferences preferences = new()
            {
                ConfirmExit = false, FirstRunCompleted = true, Language = language, Theme = "Dark", ShowHints = false,
                SavedDevices = [new("Test saved device", 1)], StartupDeviceAddress = 1, AutoConnectAtStartup = true,
                VolumeControlMode = LevelControlMode.Slider, SubwooferControlMode = LevelControlMode.Slider
            };
            Assert.True(preferences.TrySave(out _, path));
            Localization.SetLanguage(language);
            FakeSoundbarController controller = new();
            List<string> announcements = [];
            MainWindow window = new(controller, null, path, new FakeStartupRegistration(), announceState: announcements.Add)
            {
                ShowActivated = false, ShowInTaskbar = false
            };
            window.Show();
            await WaitForAsync(() => controller.IsConnected && window.IsLoaded);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            T Control<T>(string name) where T : FrameworkElement => Assert.IsType<T>(window.FindName(name));
            ConfirmedSlider volume = Control<ConfirmedSlider>("VolumeSlider");
            ConfirmedSlider subwoofer = Control<ConfirmedSlider>("SubwooferSlider");
            IRangeValueProvider Range(ConfirmedSlider slider) => Assert.IsAssignableFrom<IRangeValueProvider>(
                UIElementAutomationPeer.CreatePeerForElement(slider)!.GetPattern(PatternInterface.RangeValue));
            IRangeValueProvider volumeRange = Range(volume);
            IRangeValueProvider subwooferRange = Range(subwoofer);
            Assert.Equal(20, volumeRange.Value);
            Assert.Equal(0, volumeRange.Minimum);
            Assert.Equal(50, volumeRange.Maximum);
            Assert.Equal(1, volumeRange.SmallChange);
            Assert.Equal(-4, subwooferRange.Minimum);
            Assert.Equal(4, subwooferRange.Maximum);
            Assert.False(volumeRange.IsReadOnly);
            Assert.Equal(Visibility.Collapsed, Control<WrapPanel>("VolumeButtons").Visibility);
            window.Activate();
            Control<TextBlock>("ReadoutStatus").Focus();
            HashSet<string> visited = [];
            for (int step = 0; step < 40; step++)
            {
                if (Keyboard.FocusedElement is not FrameworkElement focused)
                    break;
                visited.Add(focused.Name);
                if (!focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)))
                    break;
            }
            foreach (string prefix in new[] { "Power", "Input", "Volume", "Mute", "Subwoofer", "Mode", "ClearVoice", "Bass", "Brightness" })
            {
                TextBlock readout = Control<TextBlock>(prefix + "StaticReadout");
                Assert.True(readout.Focusable);
                Assert.True(KeyboardNavigation.GetIsTabStop(readout));
                Assert.Contains(readout.Name, visited);
                Assert.Equal(readout.Text, UIElementAutomationPeer.CreatePeerForElement(readout)!.GetName());
            }
            Assert.Equal(Control<TextBlock>("ConnectionSummary").Text, AutomationProperties.GetName(Control<TextBlock>("ConnectionSummary")));
            Assert.True(volume.Focus());
            Assert.True(volume.IsKeyboardFocusWithin);
            announcements.Clear();
            volumeRange.SetValue(21);
            await WaitForAsync(() => volumeRange.Value == 21 && !Control<Button>("CancelButton").IsEnabled);
            Assert.Empty(announcements);
            Assert.Throws<ArgumentOutOfRangeException>(() => volumeRange.SetValue(51));
            Assert.Throws<ArgumentOutOfRangeException>(() => volumeRange.SetValue(20.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => volumeRange.SetValue(double.NaN));
            Slider.IncreaseSmall.Execute(null, volume);
            await WaitForAsync(() => volumeRange.Value == 22 && !Control<Button>("CancelButton").IsEnabled);
            Slider.DecreaseSmall.Execute(null, volume);
            await WaitForAsync(() => volumeRange.Value == 21 && !Control<Button>("CancelButton").IsEnabled);
            int priorRequests = controller.VolumeRequests;
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            controller.MutationGate = gate;
            volumeRange.SetValue(25);
            volumeRange.SetValue(26);
            volumeRange.SetValue(35);
            Assert.Equal(21, volumeRange.Value);
            Assert.True(volume.IsEnabled);
            Assert.False(subwoofer.IsEnabled);
            Assert.True(volume.IsKeyboardFocusWithin);
            gate.SetResult();
            await WaitForAsync(() => volumeRange.Value == 35 && !Control<Button>("CancelButton").IsEnabled);
            controller.MutationGate = null;
            Assert.Equal(priorRequests + 2, controller.VolumeRequests);
            Assert.Empty(announcements);
            Assert.True(subwoofer.Focus());
            int priorCommands = controller.Commands.Count;
            subwooferRange.SetValue(4);
            await WaitForAsync(() => subwooferRange.Value == 4 && !Control<Button>("CancelButton").IsEnabled);
            Assert.Equal(priorCommands + 4, controller.Commands.Count);
            subwooferRange.SetValue(-4);
            await WaitForAsync(() => subwooferRange.Value == -4 && !Control<Button>("CancelButton").IsEnabled);
            Assert.Equal(priorCommands + 12, controller.Commands.Count);
            priorCommands = controller.Commands.Count;
            controller.PublishState(controller.State! with { Subwoofer = -3 });
            subwooferRange.SetValue(-3);
            await WaitForAsync(() => subwooferRange.Value == -3 && !Control<Button>("CancelButton").IsEnabled);
            Assert.Equal(priorCommands, controller.Commands.Count);
            Slider.MinimizeValue.Execute(null, subwoofer);
            await WaitForAsync(() => subwooferRange.Value == -4 && !Control<Button>("CancelButton").IsEnabled);
            Assert.Empty(announcements);
            Assert.True(volume.Focus());
            controller.FailNextMutation = true;
            volumeRange.SetValue(36);
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            Assert.False(volume.IsEnabled);
            Assert.Equal(35, volumeRange.Value);
            Assert.Equal(35, volume.Value);
            Assert.True(volumeRange.IsReadOnly);
            Assert.Throws<ElementNotEnabledException>(() => volumeRange.SetValue(34));
            Assert.True(Control<TextBox>("StatusMessage").IsVisible);
            Assert.Empty(announcements);
            Control<Button>("RefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => volume.IsEnabled);
            controller.DelayMutation = true;
            subwooferRange.SetValue(0);
            subwooferRange.SetValue(4);
            await WaitForAsync(() => Control<Button>("CancelButton").IsEnabled);
            Control<Button>("CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            controller.DelayMutation = false;
            Assert.False(subwoofer.IsEnabled);
            Assert.Equal(-4, subwooferRange.Value);
            Assert.Equal(-4, subwoofer.Value);
            Control<Button>("RefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => subwoofer.IsEnabled);
            Control<ComboBox>("VolumeControlModeChoice").SelectedIndex = 0;
            Assert.True(window.CommitSettings());
            Assert.Equal(Visibility.Collapsed, volume.Visibility);
            Assert.Equal(Visibility.Visible, Control<WrapPanel>("VolumeButtons").Visibility);
            Assert.Equal(Visibility.Visible, subwoofer.Visibility);
            Assert.Equal(LevelControlMode.Buttons, UiPreferences.Load(out _, path).VolumeControlMode);
            Assert.Equal(LevelControlMode.Slider, UiPreferences.Load(out _, path).SubwooferControlMode);
            Control<Button>("IncreaseVolumeButton").Focus();
            MainWindow.IncreaseVolumeCommand.Execute(null, window);
            await WaitForAsync(() => controller.State!.Volume == 36 && !Control<Button>("CancelButton").IsEnabled);
            Assert.Equal(Localization.Text("Feedback.Volume", "36"), announcements[^1]);
            using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Control<ComboBox>("VolumeControlModeChoice").SelectedIndex = 1;
                Assert.False(window.CommitSettings());
                Assert.Equal(1, Control<ComboBox>("VolumeControlModeChoice").SelectedIndex);
                Assert.Equal(Visibility.Collapsed, volume.Visibility);
            }
            Assert.True(window.CommitSettings());
            window.Close();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }
}
