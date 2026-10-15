using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static async Task ExerciseStandbyEditingAsync()
    {
        string previousLanguage = Localization.CurrentLanguage;
        try
        {
            foreach ((string language, string warning) in new[]
            {
                ("pl", "Soundbar jest wyłączony. Włącz zasilanie, aby zmieniać ustawienia."),
                ("en", "The soundbar is off. Turn on its power to change settings.")
            })
                await ExerciseStandbyEditingLanguageAsync(language, warning);
        }
        finally
        {
            Localization.SetLanguage(previousLanguage);
        }
    }

    private static async Task ExerciseStandbyEditingLanguageAsync(string language, string warning)
    {
        string path = ConfigurationTests.TestPath("standby-editing-" + language + ".json");
        SoundProfile profile = new(SoundMode.Game, true, true, 1);
        UiPreferences preferences = new()
        {
            ConfirmExit = false, FirstRunCompleted = true, Language = language, Theme = "Dark", ShowHints = false,
            ShowOperationMessages = false, AnnounceStateChanges = false,
            SavedDevices = [new("Standby test device (not hardware)", 1) { SoundProfile = profile }],
            StartupDeviceAddress = 1, AutoConnectAtStartup = true, PowerOnAtStartup = false,
            PowerOffOnExit = false, KeepPoweredOn = false, StartWithWindows = false,
            VolumeControlMode = LevelControlMode.Slider, SubwooferControlMode = LevelControlMode.Slider
        };
        Assert.True(preferences.TrySave(out _, path));
        Localization.SetLanguage(language);
        FakeSoundbarController controller = new() { InitiallyPoweredOn = false };
        List<string> confirmations = [];
        List<string> announcements = [];
        MainWindow window = new(controller, null, path, new FakeStartupRegistration(), confirm: (message, _) =>
        {
            confirmations.Add(message);
            return true;
        }, announceState: announcements.Add)
        {
            ShowActivated = false, ShowInTaskbar = false
        };
        T Control<T>(string name) where T : FrameworkElement => Assert.IsType<T>(window.FindName(name));
        IRangeValueProvider Range(ConfirmedSlider slider) => Assert.IsAssignableFrom<IRangeValueProvider>(
            UIElementAutomationPeer.CreatePeerForElement(slider)!.GetPattern(PatternInterface.RangeValue));
        async Task SettleAsync()
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
        }
        async Task ClickAsync(string name)
        {
            Button button = Control<Button>(name);
            Assert.True(button.IsEnabled, name);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await SettleAsync();
        }
        void SelectControlTab()
        {
            Control<TabItem>("ControlTab").IsSelected = true;
            window.Activate();
            window.UpdateLayout();
        }
        (string Choice, string Apply)[] selectors =
        [
            ("InputChoice", "ApplyInputButton"), ("ModeChoice", "ApplyModeButton"),
            ("BrightnessChoice", "ApplyBrightnessButton")
        ];
        (string Toggle, SoundbarCommand Enable, SoundbarCommand Disable)[] toggles =
        [
            ("MuteToggle", SoundbarCommand.MuteOn, SoundbarCommand.MuteOff),
            ("ClearVoiceToggle", SoundbarCommand.ClearVoiceOn, SoundbarCommand.ClearVoiceOff),
            ("BassToggle", SoundbarCommand.BassExtensionOn, SoundbarCommand.BassExtensionOff)
        ];
        (string Button, RoutedUICommand Command)[] adjustments =
        [
            ("IncreaseVolumeButton", MainWindow.IncreaseVolumeCommand),
            ("DecreaseVolumeButton", MainWindow.DecreaseVolumeCommand),
            ("IncreaseSubwooferButton", MainWindow.IncreaseSubwooferCommand),
            ("DecreaseSubwooferButton", MainWindow.DecreaseSubwooferCommand)
        ];
        ComboBoxItem Alternative(ComboBox choice) => choice.Items.Cast<ComboBoxItem>()
            .First(item => !ReferenceEquals(item, choice.SelectedItem));
        async Task SetAutomaticAsync(bool automatic)
        {
            Control<CheckBox>("AutoApplySelectionsCheckBox").IsChecked = automatic;
            Assert.True(window.CommitSettings());
            await SettleAsync();
            Assert.Equal(automatic, UiPreferences.Load(out _, path).AutoApplySelections);
        }
        async Task SetLevelModeAsync(LevelControlMode mode)
        {
            int index = mode == LevelControlMode.Slider ? 1 : 0;
            Control<ComboBox>("VolumeControlModeChoice").SelectedIndex = index;
            Control<ComboBox>("SubwooferControlModeChoice").SelectedIndex = index;
            Assert.True(window.CommitSettings());
            await SettleAsync();
            UiPreferences saved = UiPreferences.Load(out _, path);
            Assert.Equal(mode, saved.VolumeControlMode);
            Assert.Equal(mode, saved.SubwooferControlMode);
        }
        async Task RefuseAsync(string surface, Action attempt, FrameworkElement? focusTarget = null)
        {
            await ClickAsync("RefreshButton");
            Assert.Equal(Visibility.Collapsed, Control<TextBox>("StatusMessage").Visibility);
            Assert.False(Control<CheckBox>("ShowOperationMessagesCheckBox").IsChecked);
            SelectControlTab();
            FrameworkElement target = focusTarget ?? Control<CheckBox>("PowerToggle");
            Assert.True(target.IsVisible && target.IsEnabled && target.Focusable, surface);
            Assert.True(target.Focus(), surface);
            IInputElement? focused = Keyboard.FocusedElement;
            Assert.True(target.IsKeyboardFocusWithin, surface);
            SoundbarState before = controller.State!;
            Assert.False(before.Power);
            int commands = controller.Commands.Count;
            int volumes = controller.VolumeRequests;
            int operations = controller.Operations.Count;
            int completed = controller.CompletedMutations;
            int confirmationCount = confirmations.Count;
            string saved = File.ReadAllText(path);
            object?[] selections = selectors.Select(entry => Control<ComboBox>(entry.Choice).SelectedItem).ToArray();
            attempt();
            await SettleAsync();
            Assert.Equal(commands, controller.Commands.Count);
            Assert.Equal(volumes, controller.VolumeRequests);
            Assert.Equal(operations, controller.Operations.Count);
            Assert.Equal(completed, controller.CompletedMutations);
            Assert.Equal(confirmationCount, confirmations.Count);
            Assert.Equal(saved, File.ReadAllText(path));
            Assert.Equal(before, controller.State);
            Assert.True(controller.IsConnected, surface);
            Assert.False(Control<CheckBox>("PowerToggle").IsChecked);
            Assert.Equal(before.Muted, Control<CheckBox>("MuteToggle").IsChecked);
            Assert.Equal(before.ClearVoice, Control<CheckBox>("ClearVoiceToggle").IsChecked);
            Assert.Equal(before.BassExtension, Control<CheckBox>("BassToggle").IsChecked);
            for (int index = 0; index < selectors.Length; index++)
                Assert.Same(selections[index], Control<ComboBox>(selectors[index].Choice).SelectedItem);
            foreach ((string name, int value) in new[] { ("VolumeSlider", before.Volume), ("SubwooferSlider", before.Subwoofer) })
            {
                ConfirmedSlider slider = Control<ConfirmedSlider>(name);
                Assert.True(slider.IsEnabled, surface);
                Assert.True(Range(slider).IsReadOnly, surface);
                Assert.Equal(value, slider.ConfirmedValue);
                Assert.Equal(slider.ConfirmedValue, slider.Value);
                Assert.Equal(slider.ConfirmedValue, Range(slider).Value);
            }
            TextBox status = Control<TextBox>("StatusMessage");
            Assert.Equal(warning, status.Text);
            Assert.True(status.IsVisible && status.IsReadOnly, surface);
            Assert.Equal(Visibility.Visible, Control<Label>("OperationMessageLabel").Visibility);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Assert.StartsWith(Localization.Text("Readout.Current"), Control<TextBlock>("ReadoutStatus").Text);
            Assert.Same(focused, Keyboard.FocusedElement);
            Assert.True(target.IsEnabled && target.IsKeyboardFocusWithin, surface);
            Assert.Empty(announcements);
        }
        async Task ReplayCommandAsync(Action attempt, SoundbarCommand expected)
        {
            int commands = controller.Commands.Count;
            int volumes = controller.VolumeRequests;
            attempt();
            await SettleAsync();
            Assert.Equal(commands + 1, controller.Commands.Count);
            Assert.Equal(expected, controller.Commands[^1]);
            Assert.Equal(volumes, controller.VolumeRequests);
            Assert.Equal(expected != SoundbarCommand.PowerOff, controller.State!.Power);
        }
        try
        {
            window.Show();
            await WaitForAsync(() => controller.IsConnected && window.IsLoaded);
            await SettleAsync();
            Assert.Equal(warning, Localization.Text("State.PowerOffEditBlocked"));
            Assert.False(controller.State!.Power);
            Assert.Empty(controller.Commands);
            Assert.Equal(0, controller.VolumeRequests);
            SelectControlTab();
            Assert.True(Control<CheckBox>("PowerToggle").Focus());
            HashSet<string> visited = [];
            for (int step = 0; step < 70; step++)
            {
                if (Keyboard.FocusedElement is not FrameworkElement focused)
                    break;
                visited.Add(focused.Name);
                if (!focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)))
                    break;
            }
            foreach (string name in new[]
            {
                "PowerToggle", "InputChoice", "ModeChoice", "BrightnessChoice", "MuteToggle",
                "ClearVoiceToggle", "BassToggle", "VolumeSlider", "SubwooferSlider"
            })
                Assert.Contains(name, visited);

            window.OpenSettingsWindow();
            await SettleAsync();
            SelectSettingsTab(window, "ControlsSettingsTab");
            CheckBox hints = Control<CheckBox>("ShowHintsCheckBox");
            Assert.True(hints.IsEnabled && hints.Focus());
            hints.IsChecked = true;
            Assert.True(window.CommitSettings());
            Assert.True(UiPreferences.Load(out _, path).ShowHints);
            hints.IsChecked = false;
            Assert.True(window.CommitSettings());
            Assert.False(UiPreferences.Load(out _, path).ShowHints);
            window.CloseSectionWindows();
            SectionWindow help = window.OpenHelpWindow();
            await SettleAsync();
            Assert.True(help.IsVisible);
            Assert.True(Control<TextBox>("HelpReadout").IsReadOnly);
            Assert.True(Control<TextBox>("HelpReadout").Focus());
            window.CloseSectionWindows();
            SelectControlTab();
            Control<CheckBox>("CloseToTrayCheckBox").IsChecked = true;
            Assert.True(window.CommitSettings());
            await SettleAsync();
            TrayController tray = Assert.IsType<TrayController>(window.Tray);
            MenuItem[] trayActions = selectors.SelectMany(entry => Control<ComboBox>(entry.Choice).Items.Cast<ComboBoxItem>())
                .Select(item => tray.CommandItem(Assert.IsType<SoundbarCommand>(item.Tag)))
                .Concat(new[]
                {
                    tray.FindItem("Control.Mute"), tray.FindItem("Control.ClearVoice"), tray.FindItem("Control.BassExtension"),
                    tray.CommandItem(SoundbarCommand.SubwooferUp), tray.CommandItem(SoundbarCommand.SubwooferDown),
                    tray.FindItem("Command.IncreaseVolume"), tray.FindItem("Command.DecreaseVolume")
                }).ToArray();

            async Task ExerciseBlockedSurfacesAsync()
            {
                SelectControlTab();
                foreach (bool automatic in new[] { true, false })
                {
                    await SetAutomaticAsync(automatic);
                    foreach ((string choiceName, string applyName) in selectors)
                    {
                        ComboBox choice = Control<ComboBox>(choiceName);
                        foreach (ComboBoxItem item in choice.Items.Cast<ComboBoxItem>().Where(item => !ReferenceEquals(item, choice.SelectedItem)).ToArray())
                            await RefuseAsync(choiceName, () => choice.SelectedItem = item, choice);
                        if (!automatic)
                        {
                            Button apply = Control<Button>(applyName);
                            await RefuseAsync(applyName, () => apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), apply);
                        }
                    }
                }
                foreach (var entry in toggles)
                {
                    CheckBox toggle = Control<CheckBox>(entry.Toggle);
                    await RefuseAsync(entry.Toggle, () => toggle.IsChecked = toggle.IsChecked != true, toggle);
                }
                foreach (string name in new[] { "VolumeSlider", "SubwooferSlider" })
                {
                    ConfirmedSlider slider = Control<ConfirmedSlider>(name);
                    await RefuseAsync(name + " UIA", () => Range(slider).SetValue(slider.ConfirmedValue + 1), slider);
                    await RefuseAsync(name + " unchanged UIA", () => Range(slider).SetValue(slider.ConfirmedValue), slider);
                    await RefuseAsync(name + " value", () => slider.Value = slider.ConfirmedValue - 1, slider);
                    foreach (RoutedCommand command in new[] { Slider.IncreaseSmall, Slider.DecreaseSmall, Slider.MinimizeValue, Slider.MaximizeValue })
                        await RefuseAsync(name + " " + command.Name, () => command.Execute(null, slider), slider);
                }
                await SetLevelModeAsync(LevelControlMode.Buttons);
                foreach ((string name, RoutedUICommand command) in adjustments)
                {
                    Button button = Control<Button>(name);
                    Assert.True(command.CanExecute(null, window), name);
                    await RefuseAsync(name + " routed", () => command.Execute(null, window), button);
                    IInvokeProvider invoke = Assert.IsAssignableFrom<IInvokeProvider>(
                        UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke));
                    await RefuseAsync(name + " button", invoke.Invoke, button);
                }
                await SetLevelModeAsync(LevelControlMode.Slider);
                foreach (MenuItem item in trayActions)
                {
                    Assert.True(item.IsEnabled, AutomationProperties.GetAutomationId(item));
                    bool checkedBefore = item.IsChecked;
                    await RefuseAsync("tray " + AutomationProperties.GetAutomationId(item), () => TrayController.InvokeItem(item));
                    Assert.Equal(checkedBefore, item.IsChecked);
                }
                foreach (string name in new[] { "ApplySoundProfileButton", "SaveSoundProfileButton" })
                {
                    Button button = Control<Button>(name);
                    Assert.True(button.IsEnabled, name);
                    await RefuseAsync(name, () => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                }
                Assert.Equal(profile, Assert.Single(UiPreferences.Load(out _, path).SavedDevices).SoundProfile);
            }

            await ExerciseBlockedSurfacesAsync();
            SoundbarState initialStandby = controller.State!;
            foreach (bool maximum in new[] { false, true })
            {
                controller.PublishState(initialStandby with { Volume = maximum ? 50 : 0, Subwoofer = maximum ? 4 : -4 });
                await SettleAsync();
                foreach (string name in new[] { "VolumeSlider", "SubwooferSlider" })
                {
                    ConfirmedSlider slider = Control<ConfirmedSlider>(name);
                    await RefuseAsync(name + " boundary UIA", () => Range(slider).SetValue(slider.ConfirmedValue), slider);
                    foreach (Key key in new[] { Key.Home, Key.End, Key.Left, Key.Right, Key.PageUp, Key.PageDown })
                    {
                        await RefuseAsync(name + " boundary " + key, () =>
                        {
                            PresentationSource source = PresentationSource.FromVisual(slider)!;
                            Assert.NotNull(source);
                            KeyEventArgs keyEvent = new(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
                            {
                                RoutedEvent = Keyboard.PreviewKeyDownEvent
                            };
                            slider.RaiseEvent(keyEvent);
                            Assert.True(keyEvent.Handled);
                        }, slider);
                    }
                }
                foreach (var entry in adjustments)
                {
                    Assert.True(entry.Command.CanExecute(null, window), entry.Button);
                    await RefuseAsync(entry.Button + " boundary", () => entry.Command.Execute(null, window));
                }
            }
            controller.PublishState(initialStandby);
            await SettleAsync();
            Assert.Empty(controller.Commands);
            Assert.Equal(0, controller.VolumeRequests);
            await ReplayCommandAsync(() => Control<CheckBox>("PowerToggle").IsChecked = true, SoundbarCommand.PowerOn);
            Assert.True(Control<CheckBox>("PowerToggle").IsChecked);
            foreach (string name in new[] { "VolumeSlider", "SubwooferSlider" })
                Assert.False(Range(Control<ConfirmedSlider>(name)).IsReadOnly);
            foreach (bool automatic in new[] { true, false })
            {
                await SetAutomaticAsync(automatic);
                foreach ((string choiceName, string applyName) in selectors)
                {
                    ComboBox choice = Control<ComboBox>(choiceName);
                    ComboBoxItem next = Alternative(choice);
                    SoundbarCommand expected = Assert.IsType<SoundbarCommand>(next.Tag);
                    if (automatic)
                        await ReplayCommandAsync(() => choice.SelectedItem = next, expected);
                    else
                    {
                        int commands = controller.Commands.Count;
                        choice.SelectedItem = next;
                        await SettleAsync();
                        Assert.Equal(commands, controller.Commands.Count);
                        await ReplayCommandAsync(() => Control<Button>(applyName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), expected);
                    }
                    Assert.Same(next, choice.SelectedItem);
                }
            }
            foreach (var entry in toggles)
            {
                CheckBox toggle = Control<CheckBox>(entry.Toggle);
                await ReplayCommandAsync(() => toggle.IsChecked = true, entry.Enable);
                await ReplayCommandAsync(() => toggle.IsChecked = false, entry.Disable);
            }
            ConfirmedSlider volume = Control<ConfirmedSlider>("VolumeSlider");
            ConfirmedSlider subwoofer = Control<ConfirmedSlider>("SubwooferSlider");
            int volumeRequests = controller.VolumeRequests;
            Range(volume).SetValue(21);
            await SettleAsync();
            Assert.Equal(volumeRequests + 1, controller.VolumeRequests);
            Assert.Equal(21, controller.State!.Volume);
            Assert.Equal(21, volume.Value);
            Assert.Equal(21, Range(volume).Value);
            await ReplayCommandAsync(() => Range(subwoofer).SetValue(1), SoundbarCommand.SubwooferUp);
            Assert.Equal(1, controller.State.Subwoofer);
            Assert.Equal(1, subwoofer.Value);
            Assert.Equal(1, Range(subwoofer).Value);
            await SetLevelModeAsync(LevelControlMode.Buttons);
            foreach ((string name, RoutedUICommand command) in adjustments)
            {
                Assert.True(command.CanExecute(null, window), name);
                SoundbarState before = controller.State!;
                command.Execute(null, window);
                await SettleAsync();
                int direction = name.StartsWith("Increase", StringComparison.Ordinal) ? 1 : -1;
                Assert.Equal(before.Volume + (name.Contains("Volume", StringComparison.Ordinal) ? direction : 0), controller.State!.Volume);
                Assert.Equal(before.Subwoofer + (name.Contains("Subwoofer", StringComparison.Ordinal) ? direction : 0), controller.State.Subwoofer);
            }
            await SetLevelModeAsync(LevelControlMode.Slider);
            await ReplayCommandAsync(() => TrayController.InvokeItem(tray.CommandItem(SoundbarCommand.Game)), SoundbarCommand.Game);
            volumeRequests = controller.VolumeRequests;
            TrayController.InvokeItem(tray.FindItem("Command.DecreaseVolume"));
            await SettleAsync();
            Assert.Equal(volumeRequests + 1, controller.VolumeRequests);
            Assert.Equal(20, controller.State!.Volume);
            await ClickAsync("ApplySoundProfileButton");
            Assert.Equal(profile.Mode, controller.State.Mode);
            Assert.Equal(profile.ClearVoice, controller.State.ClearVoice);
            Assert.Equal(profile.BassExtension, controller.State.BassExtension);
            Assert.Equal(profile.Subwoofer, controller.State.Subwoofer);
            await ClickAsync("SaveSoundProfileButton");
            Assert.Equal(profile, Assert.Single(UiPreferences.Load(out _, path).SavedDevices).SoundProfile);
            Assert.Equal(2, confirmations.Count);

            await SetAutomaticAsync(false);
            object?[] confirmedSelections = selectors.Select(entry => Control<ComboBox>(entry.Choice).SelectedItem).ToArray();
            int commandsBeforeDrafts = controller.Commands.Count;
            volumeRequests = controller.VolumeRequests;
            foreach (var entry in selectors)
            {
                ComboBox choice = Control<ComboBox>(entry.Choice);
                choice.SelectedItem = Alternative(choice);
            }
            await SettleAsync();
            controller.PublishState(controller.State! with { Power = false });
            await SettleAsync();
            Assert.False(Control<CheckBox>("PowerToggle").IsChecked);
            for (int index = 0; index < selectors.Length; index++)
                Assert.Same(confirmedSelections[index], Control<ComboBox>(selectors[index].Choice).SelectedItem);
            Assert.Equal(commandsBeforeDrafts, controller.Commands.Count);
            Assert.Equal(volumeRequests, controller.VolumeRequests);
            await ExerciseBlockedSurfacesAsync();
            await ReplayCommandAsync(() => TrayController.InvokeItem(tray.FindItem("Control.Power")), SoundbarCommand.PowerOn);
            Assert.True(tray.FindItem("Control.Power").IsChecked);
            for (int index = 0; index < selectors.Length; index++)
            {
                Assert.Same(confirmedSelections[index], Control<ComboBox>(selectors[index].Choice).SelectedItem);
                string applyName = selectors[index].Apply;
                SoundbarCommand expected = Assert.IsType<SoundbarCommand>(Assert.IsType<ComboBoxItem>(confirmedSelections[index]).Tag);
                await ReplayCommandAsync(() => Control<Button>(applyName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), expected);
            }
            await ReplayCommandAsync(() => Control<CheckBox>("PowerToggle").IsChecked = false, SoundbarCommand.PowerOff);
            int commandsBeforeManagement = controller.Commands.Count;
            volumeRequests = controller.VolumeRequests;
            Control<TabItem>("DevicesTab").IsSelected = true;
            await SettleAsync();
            Assert.True(Control<ListBox>("SavedDevicesList").IsEnabled);
            Assert.True(Control<Button>("DisconnectButton").Focus());
            await ClickAsync("DisconnectButton");
            Assert.False(controller.IsConnected);
            await ClickAsync("SearchButton");
            Assert.NotEmpty(Control<ListBox>("DevicesList").Items);
            Control<ListBox>("DevicesList").SelectedIndex = 0;
            await ClickAsync("ConnectButton");
            Assert.True(controller.IsConnected);
            Assert.False(controller.State!.Power);
            Assert.Equal(commandsBeforeManagement, controller.Commands.Count);
            Assert.Equal(volumeRequests, controller.VolumeRequests);
            await ClickAsync("RefreshButton");
            SelectControlTab();
            Assert.True(Control<CheckBox>("PowerToggle").IsEnabled);
        }
        finally
        {
            window.CloseSectionWindows();
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }
}
