using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static async Task ExerciseGlobalHotkeysAsync()
    {
        foreach (string language in new[] { "pl", "en" })
        {
            Localization.SetLanguage(language);
            string path = ConfigurationTests.TestPath("hotkey-window-" + language + ".json");
            Assert.True(new UiPreferences { Language = language, Theme = "Dark", FirstRunCompleted = true, ConfirmExit = false }.TrySave(out _, path));
            WindowHotkeyBackend backend = new();
            FakeSoundbarController controller = new();
            bool approve = true;
            MainWindow window = new(controller, null, path, new FakeStartupRegistration(),
                (_, _) => approve, hotkeyBackend: backend);
            window.Show();
            await SettleReadoutWindowAsync(window);
            T Control<T>(string name) where T : FrameworkElement => Assert.IsType<T>(window.FindName(name));
            SectionWindow settings = window.OpenSettingsWindow();
            SelectSettingsTab(window, "HotkeysSettingsTab");
            typeof(Window).GetMethod("OnContentRendered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(settings, [EventArgs.Empty]);
            Assert.Same(Control<TabItem>("HotkeysSettingsTab"), Control<TabControl>("SettingsTabs").SelectedItem);
            await SettleReadoutWindowAsync(window);
            TextBox[] fields = RefinementLogicalDescendants(Control<TabItem>("HotkeysSettingsTab")).OfType<TextBox>().ToArray();
            Assert.Equal(Enum.GetValues<HotkeyAction>().Length, fields.Length);
            TextBox Field(HotkeyAction action) => fields.Single(field => AutomationProperties.GetAutomationId(field) == "Hotkey." + action);
            foreach (TextBox field in fields)
            {
                Assert.NotEmpty(AutomationProperties.GetName(field));
                Label label = Assert.IsType<Label>(AutomationProperties.GetLabeledBy(field));
                Assert.Same(field, label.Target);
                Grid row = Assert.IsType<Grid>(field.Parent);
                Button capture = Assert.IsType<Button>(row.Children[2]);
                Assert.True(field.IsTabStop && capture.IsTabStop);
                Assert.NotEmpty(AutomationProperties.GetName(capture));
            }
            byte[] baseline = File.ReadAllBytes(path);
            Field(HotkeyAction.VolumeUp).Text = "Ctrl+Alt+Shift+F10";
            Control<CheckBox>("GlobalHotkeysCheckBox").IsChecked = true;
            Assert.Empty(backend.Registered);
            Assert.Equal(baseline, File.ReadAllBytes(path));
            Assert.True(window.CommitSettings());
            Assert.Equal(7, backend.Registered.Count);
            Assert.True(UiPreferences.Load(out _, path).GlobalHotkeysEnabled);
            baseline = File.ReadAllBytes(path);
            Field(HotkeyAction.VolumeDown).Text = Field(HotkeyAction.VolumeUp).Text;
            Assert.False(window.CommitSettings());
            Assert.Equal(baseline, File.ReadAllBytes(path));
            Assert.Equal(7, backend.Registered.Count);
            Assert.True(window.CancelSettings());
            Field(HotkeyAction.VolumeUp).Text = "Ctrl+Alt+Shift+F11";
            backend.FailRegister = true;
            Assert.False(window.CommitSettings());
            Assert.Equal(baseline, File.ReadAllBytes(path));
            Assert.Equal(7, backend.Registered.Count);
            backend.FailRegister = false;
            using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.False(window.CommitSettings());
            Assert.Equal(baseline, File.ReadAllBytes(path));
            Assert.Equal(7, backend.Registered.Count);
            Assert.True(window.CancelSettings());
            Control<CheckBox>("GlobalHotkeysCheckBox").IsChecked = false;
            Assert.True(window.CommitSettings());
            Assert.Empty(backend.Registered);
            Assert.Equal("Ctrl+Alt+Shift+F10", UiPreferences.Load(out _, path).Hotkeys.Single(binding => binding.Action == HotkeyAction.VolumeUp).Gesture);
            approve = false;
            Control<Button>("ResetHotkeysSettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Ctrl+Alt+Shift+F10", Field(HotkeyAction.VolumeUp).Text);
            approve = true;
            Control<Button>("ResetHotkeysSettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Ctrl+Alt+Shift+Up", Field(HotkeyAction.VolumeUp).Text);
            Assert.False(Control<CheckBox>("GlobalHotkeysCheckBox").IsChecked);
            Assert.Equal("Ctrl+Alt+Shift+F10", UiPreferences.Load(out _, path).Hotkeys.Single(binding => binding.Action == HotkeyAction.VolumeUp).Gesture);
            Assert.True(window.CancelSettings());
            Control<CheckBox>("GlobalHotkeysCheckBox").IsChecked = true;
            Assert.True(window.CommitSettings());
            Assert.NotEmpty(backend.Registered);
            Control<Button>("ResetHotkeysSettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.CommitSettings());
            Assert.Empty(backend.Registered);
            Assert.Equal(HotkeyDefaults.Create(), UiPreferences.Load(out _, path).Hotkeys);
            settings.Close();
            Control<Button>("SearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            Control<ListBox>("DevicesList").SelectedIndex = 0;
            Control<Button>("ConnectButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            TextBox otherContent = new();
            Window otherWindow = new() { Content = otherContent, Width = 200, Height = 100, ShowInTaskbar = false };
            otherWindow.Show();
            Assert.True(otherContent.Focus());
            await window.ExecuteGlobalHotkeyAsync(HotkeyAction.VolumeUp);
            Assert.Equal(21, controller.State!.Volume);
            Assert.Same(otherContent, Keyboard.FocusedElement);
            await window.ExecuteGlobalHotkeyAsync(HotkeyAction.VolumeDown);
            Assert.Equal(20, controller.State!.Volume);
            foreach ((HotkeyAction action, SoundbarCommand command) in new[]
            {
                (HotkeyAction.SubwooferUp, SoundbarCommand.SubwooferUp), (HotkeyAction.SubwooferDown, SoundbarCommand.SubwooferDown),
                (HotkeyAction.ToggleMute, SoundbarCommand.MuteOn), (HotkeyAction.ToggleMute, SoundbarCommand.MuteOff),
                (HotkeyAction.InputOptical, SoundbarCommand.InputOptical), (HotkeyAction.InputBluetooth, SoundbarCommand.InputBluetooth),
                (HotkeyAction.InputAnalog, SoundbarCommand.InputAnalog), (HotkeyAction.InputTv, SoundbarCommand.InputTv),
                (HotkeyAction.ModeStereo, SoundbarCommand.Stereo), (HotkeyAction.ModeStandard, SoundbarCommand.Standard),
                (HotkeyAction.ModeMovie3D, SoundbarCommand.Movie3D), (HotkeyAction.ModeGame, SoundbarCommand.Game),
                (HotkeyAction.ToggleClearVoice, SoundbarCommand.ClearVoiceOn), (HotkeyAction.ToggleClearVoice, SoundbarCommand.ClearVoiceOff),
                (HotkeyAction.ToggleBassExtension, SoundbarCommand.BassExtensionOn), (HotkeyAction.ToggleBassExtension, SoundbarCommand.BassExtensionOff),
                (HotkeyAction.BrightnessDim, SoundbarCommand.IndicatorsDim), (HotkeyAction.BrightnessOff, SoundbarCommand.IndicatorsOff),
                (HotkeyAction.BrightnessBright, SoundbarCommand.IndicatorsBright), (HotkeyAction.TogglePower, SoundbarCommand.PowerOff)
            })
            {
                await window.ExecuteGlobalHotkeyAsync(action);
                Assert.Equal(command, controller.Commands[^1]);
            }
            int count = controller.Commands.Count;
            await window.ExecuteGlobalHotkeyAsync(HotkeyAction.ToggleClearVoice);
            Assert.Equal(count, controller.Commands.Count);
            await window.ExecuteGlobalHotkeyAsync(HotkeyAction.TogglePower);
            Assert.True(controller.State!.Power);
            Assert.Same(otherContent, Keyboard.FocusedElement);
            otherWindow.Close();
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
            Assert.Empty(backend.Registered);
        }
    }

    private sealed class WindowHotkeyBackend : IHotkeyBackend
    {
        internal Dictionary<int, (uint Modifiers, uint Key)> Registered { get; } = [];
        internal bool FailRegister { get; set; }
        public bool TryRegister(int id, uint modifiers, uint virtualKey, out int win32Code)
        {
            win32Code = FailRegister ? 1409 : 0;
            if (FailRegister)
                return false;
            Registered.Add(id, (modifiers, virtualKey));
            return true;
        }
        public bool TryUnregister(int id, out int win32Code)
        {
            win32Code = 0;
            return Registered.Remove(id);
        }
    }
}
