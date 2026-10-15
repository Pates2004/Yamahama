using System.IO;
using System.Text.Json;
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
    [Fact]
    public async Task ControlsSendVerifiedCommandsAndDoNotInventState()
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                TestApplication application = new();
#pragma warning disable WPF0001
                application.ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
                application.Resources = new ResourceDictionary();
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml")
                });
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/Yamahama;component/Styles.xaml", UriKind.Relative)
                });
                application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                application.StartTest = async () =>
                {
                    try
                    {
                        Localization.Initialize();
                        Localization.SetLanguage("pl");
                        ExerciseFirstRun();
                        await ExerciseSingleInstanceRestorationAsync();
                        Localization.SetLanguage("pl");
                        await ExerciseSettingsRefinementsAsync();
                        await ExerciseFocusRefinementsAsync();
                        await ExerciseSectionsAsync();
                        await ExerciseExitConfirmationAsync();
                        await ExerciseFeedbackAsync();
                        await ExerciseReadoutModesAsync();
                        await ExerciseLevelsAsync();
                        await ExerciseStandbyEditingAsync();
                        await ExerciseAutoConnectAsync();
                        await ExercisePowerProtectionAsync();
                        await ExerciseExitPowerAsync();
                        await ExerciseDeviceStorageAsync();
                        await ExerciseTrayAsync();
                        await ExerciseStartupAndResetAsync();
                        await ExerciseSoundProfilesAsync();
                        await ExerciseNamedSoundProfilesAsync();
                        await ExerciseSettingsTransactionsAsync();
                        await ExerciseWindowAsync();
                        completion.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                    finally
                    {
                        application.Shutdown();
                    }
                };
                application.Run();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }) { IsBackground = true, Name = "WPF integration validation" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(120));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "The WPF test dispatcher did not exit.");
    }

    private static void ExerciseFirstRun()
    {
        string path = ConfigurationTests.TestPath("setup.json");
        File.WriteAllText(path, "{}");
        FirstRunWindow RunDialog(Action<FirstRunWindow> action, string? configurationPath = null)
        {
            FirstRunWindow dialog = new(new UiPreferences(), configurationPath ?? path)
            {
                ShowActivated = false, ShowInTaskbar = false, WindowState = WindowState.Minimized
            };
            Exception? failure = null;
            dialog.Dispatcher.BeginInvoke(() =>
            {
                try { action(dialog); }
                catch (Exception exception) { failure = exception; }
                finally { if (dialog.IsVisible) dialog.Close(); }
            }, DispatcherPriority.ApplicationIdle);
            dialog.ShowDialog();
            if (failure is not null)
                throw failure;
            return dialog;
        }
        static T Control<T>(FirstRunWindow dialog, string name) where T : FrameworkElement => Assert.IsType<T>(dialog.FindName(name));
        FirstRunWindow dismissed = RunDialog(dialog =>
        {
            Assert.Equal(-1, Control<ComboBox>(dialog, "LanguageChoice").SelectedIndex);
            Assert.Equal(-1, Control<ComboBox>(dialog, "ThemeChoice").SelectedIndex);
            Assert.False(Control<Button>(dialog, "ContinueButton").IsEnabled);
            Assert.False(Control<Button>(dialog, "CancelButton").IsCancel);
            Control<ComboBox>(dialog, "LanguageChoice").SelectedIndex = 1;
            Assert.False(Control<Button>(dialog, "ContinueButton").IsEnabled);
            Control<Button>(dialog, "CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert.Null(dismissed.CompletedConfiguration);
        Assert.Equal("{}", File.ReadAllText(path));
        FirstRunWindow closed = RunDialog(dialog =>
        {
            Assert.Equal(-1, Control<ComboBox>(dialog, "LanguageChoice").SelectedIndex);
            Control<ComboBox>(dialog, "ThemeChoice").SelectedIndex = 0;
            Assert.False(Control<Button>(dialog, "ContinueButton").IsEnabled);
            dialog.Close();
        });
        Assert.Null(closed.CompletedConfiguration);
        Assert.Equal("{}", File.ReadAllText(path));
        FirstRunWindow completed = RunDialog(dialog =>
        {
            Control<ComboBox>(dialog, "LanguageChoice").SelectedIndex = 1;
            Control<ComboBox>(dialog, "ThemeChoice").SelectedIndex = 1;
            Control<ComboBox>(dialog, "TextScaleChoice").SelectedValue = 150;
            Assert.Equal(SystemFonts.MessageFontSize * 1.5, dialog.FontSize);
            Control<CheckBox>(dialog, "ShowHintsChoice").IsChecked = false;
            Assert.False(Control<CheckBox>(dialog, "ShowOperationMessagesChoice").IsChecked);
            Assert.Equal(ValueDisplayMode.StaticText, Assert.IsType<ComboBoxItem>(Control<ComboBox>(dialog, "ValueDisplayModeChoice").SelectedItem).Tag);
            Assert.True(Control<CheckBox>(dialog, "AnnounceStateChangesChoice").IsChecked);
            Control<CheckBox>(dialog, "ShowOperationMessagesChoice").IsChecked = true;
            Control<ComboBox>(dialog, "ValueDisplayModeChoice").SelectedIndex = 0;
            Control<CheckBox>(dialog, "AnnounceStateChangesChoice").IsChecked = false;
            Assert.True(Control<Button>(dialog, "ContinueButton").IsEnabled);
            Control<Button>(dialog, "ContinueButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert.True(completed.CompletedConfiguration?.IsSetupComplete);
        UiPreferences saved = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Equal("en", saved.Language);
        Assert.Equal("Dark", saved.Theme);
        Assert.False(saved.ShowHints);
        Assert.Equal(150, saved.TextScale);
        Assert.True(saved.ShowOperationMessages);
        Assert.Equal(ValueDisplayMode.ReadOnlyFields, saved.ValueDisplayMode);
        Assert.False(saved.AnnounceStateChanges);
        FirstRunWindow saveFailure = RunDialog(dialog =>
        {
            Control<ComboBox>(dialog, "LanguageChoice").SelectedIndex = 0;
            Control<ComboBox>(dialog, "ThemeChoice").SelectedIndex = 0;
            Control<Button>(dialog, "ContinueButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(dialog.IsVisible);
            Assert.Null(dialog.CompletedConfiguration);
            Assert.Equal(Localization.Text("Setup.SaveError"), Control<TextBox>(dialog, "SetupStatus").Text);
        }, Path.GetDirectoryName(path));
        Assert.Null(saveFailure.CompletedConfiguration);
    }

    private static async Task ExerciseWindowAsync()
    {
        Localization.SetLanguage("pl");
        string preferencesPath = TestPaths.ValidationFile("preferences-tests", "window-flow.json");
        Assert.True(new UiPreferences
        {
            ConfirmExit = false,
            AutoConnectAtStartup = false,
            VolumeControlMode = LevelControlMode.Buttons,
            SubwooferControlMode = LevelControlMode.Buttons
        }.TrySave(out _, preferencesPath));
        FakeSoundbarController controller = new();
        List<string> copiedAddresses = [];
        bool clipboardFailure = false;
        MainWindow window = new(controller, address =>
        {
            if (clipboardFailure)
                throw new InvalidOperationException("Test clipboard failure");
            copiedAddresses.Add(address);
        }, preferencesPath)
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowState = WindowState.Minimized
        };
        window.Show();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        T Control<T>(string name) where T : FrameworkElement => Assert.IsType<T>(window.FindName(name));
        string Read(string name) => Control<TextBox>(name).Text;
        async Task ClickAsync(string name)
        {
            Button button = Control<Button>(name);
            Assert.True(button.IsEnabled, $"Expected enabled control: {name}");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled &&
                (Control<Button>("RefreshButton").IsEnabled || Control<Button>("SearchButton").IsEnabled));
        }

        async Task ToggleAsync(string name)
        {
            CheckBox toggle = Control<CheckBox>(name);
            Assert.True(toggle.IsEnabled);
            CheckBoxAutomationPeer peer = new(toggle);
            ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!).Toggle();
            if (name is "AutoApplySelectionsCheckBox" or "ShowHintsCheckBox" or "ShowVolumeRowItem")
                Assert.True(window.CommitSettings());
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
        }

        Assert.StartsWith("Yamahama", window.Title);
        Assert.Contains("Brak odczytu", Read("FirmwareDeviceReadout"));
        Assert.Null(window.FindName("BluetoothHelpTab"));
        Assert.Null(window.FindName("BluetoothGuideReadout"));
        foreach ((string tabName, string guideName, SupportGuide guide) in new[]
        {
            ("FirmwareHelpTab", "FirmwareGuideReadout", SupportGuide.Firmware),
            ("HelpTab", "TouchPanelGuideReadout", SupportGuide.TouchPanel)
        })
        {
            if (tabName == "FirmwareHelpTab")
                window.OpenFirmwareWindow();
            else
                window.OpenHelpWindow();
            TextBox guideControl = Control<TextBox>(guideName);
            Assert.Equal(SupportResources.ReadGuide(guide), guideControl.Text);
            Assert.True(guideControl.IsReadOnly);
            Assert.True(guideControl.Focusable);
            Assert.NotEmpty(AutomationProperties.GetName(guideControl));
        }
        await ClickAsync("FirmwareDownloadsButton");
        await ClickAsync("YamahaSupportButton");
        Assert.Equal(new[] { SupportResources.FirmwareDownloadsAddress, SupportResources.YamahaSupportAddress }, copiedAddresses);
        Assert.Empty(controller.Commands);
        clipboardFailure = true;
        await ClickAsync("FirmwareDownloadsButton");
        Assert.Contains("Nie udało się skopiować", Read("StatusMessage"));
        Assert.Contains(SupportResources.FirmwareDownloadsAddress, Read("StatusMessage"));
        Assert.Equal(2, copiedAddresses.Count);
        clipboardFailure = false;
        window.CloseSectionWindows();
        Control<TabControl>("MainTabs").SelectedIndex = 0;
        Assert.False(Control<CheckBox>("PowerToggle").IsEnabled);
        Assert.Equal("Nieznana", Read("VolumeReadout"));
        await ClickAsync("SearchButton");
        Assert.Single(Control<ListBox>("DevicesList").Items.Cast<object>());
        Assert.Equal("Test soundbar (not hardware) — 00:00:00:00:00:01 — sygnał: -40 dBm",
            Control<ListBox>("DevicesList").Items[0].ToString());
        await ClickAsync("ConnectButton");
        Assert.Equal("SR-C30A", Read("ModelReadout"));
        Assert.Equal("1.23", Read("FirmwareReadout"));
        Assert.Equal("SR-C30A; firmware: 1.23", Read("FirmwareDeviceReadout"));
        Assert.Equal("20", Read("VolumeReadout"));

        Assert.Empty(controller.Commands);
        Assert.True(Control<CheckBox>("AutoApplySelectionsCheckBox").IsChecked);
        Assert.Equal(Visibility.Collapsed, Control<Button>("ApplyModeButton").Visibility);
        foreach ((string name, SoundbarCommand enabled, SoundbarCommand disabled) in new[]
        {
            ("PowerToggle", SoundbarCommand.PowerOn, SoundbarCommand.PowerOff),
            ("MuteToggle", SoundbarCommand.MuteOn, SoundbarCommand.MuteOff),
            ("ClearVoiceToggle", SoundbarCommand.ClearVoiceOn, SoundbarCommand.ClearVoiceOff),
            ("BassToggle", SoundbarCommand.BassExtensionOn, SoundbarCommand.BassExtensionOff)
        })
        {
            for (int iteration = 0; iteration < 2; iteration++)
            {
                bool requested = Control<CheckBox>(name).IsChecked != true;
                int previousCount = controller.Commands.Count;
                await ToggleAsync(name);
                Assert.Equal(previousCount + 1, controller.Commands.Count);
                Assert.Equal(requested ? enabled : disabled, controller.Commands[^1]);
                Assert.Equal(requested, Control<CheckBox>(name).IsChecked);
                Assert.Contains("potwierdziło", Read("StatusMessage"));
            }
        }

        foreach (string choiceName in new[] { "InputChoice", "ModeChoice", "BrightnessChoice" })
        {
            ComboBox choice = Control<ComboBox>(choiceName);
            ComboBoxItem next = choice.Items.Cast<ComboBoxItem>().First(item => !ReferenceEquals(item, choice.SelectedItem));
            int previousCount = controller.Commands.Count;
            choice.SelectedItem = next;
            await WaitForAsync(() => Control<Button>("RefreshButton").IsEnabled);
            Assert.Equal(previousCount + 1, controller.Commands.Count);
            Assert.Equal(Assert.IsType<SoundbarCommand>(next.Tag), controller.Commands[^1]);
            Assert.Same(next, choice.SelectedItem);
        }
        int beforeRefresh = controller.Commands.Count;
        await ClickAsync("RefreshButton");
        controller.PublishState(controller.State! with { Mode = SoundMode.Game });
        await WaitForAsync(() => (Control<ComboBox>("ModeChoice").SelectedItem as ComboBoxItem)?.Tag is SoundbarCommand.Game);
        Assert.Equal(beforeRefresh, controller.Commands.Count);

        await ToggleAsync("AutoApplySelectionsCheckBox");
        Assert.Equal(Visibility.Visible, Control<Button>("ApplyModeButton").Visibility);
        ComboBox modes = Control<ComboBox>("ModeChoice");
        SoundbarCommand[] soundModes = modes.Items.Cast<ComboBoxItem>().Select(item => Assert.IsType<SoundbarCommand>(item.Tag)).ToArray();
        Assert.Equal(new[] { SoundbarCommand.Stereo, SoundbarCommand.Standard, SoundbarCommand.Movie3D, SoundbarCommand.Game }, soundModes);
        Assert.Equal(soundModes.Length, soundModes.Distinct().Count());
        ComboBoxItem stereo = modes.Items.Cast<ComboBoxItem>().Single(item => item.Tag is SoundbarCommand.Stereo);
        modes.SelectedItem = stereo;
        await ClickAsync("RefreshButton");
        Assert.Same(stereo, modes.SelectedItem);
        Assert.Equal(SoundMode.Game, controller.State!.Mode);
        await ToggleAsync("AutoApplySelectionsCheckBox");
        Assert.Equal(beforeRefresh, controller.Commands.Count);
        Assert.Equal(SoundbarCommand.Game, ((ComboBoxItem)modes.SelectedItem).Tag);
        await ToggleAsync("AutoApplySelectionsCheckBox");

        window.OpenSettingsWindow();
        Assert.NotEmpty(AutomationProperties.GetHelpText(Control<Button>("SearchButton")));
        await ToggleAsync("ShowHintsCheckBox");
        Assert.Equal(Visibility.Collapsed, Control<TextBlock>("ControlUsageHint").Visibility);
        Assert.Empty(AutomationProperties.GetHelpText(Control<Button>("SearchButton")));
        Assert.Empty(AutomationProperties.GetHelpText(Control<TextBox>("VolumeReadout")));
        Assert.NotEmpty(AutomationProperties.GetName(Control<CheckBox>("ShowHintsCheckBox")));
        Assert.Equal(Visibility.Visible, Control<TextBlock>("ReadoutStatus").Visibility);
        Assert.Equal(Visibility.Collapsed, Control<TextBox>("DeviceMemoryHint").Visibility);
        Assert.Equal(beforeRefresh, controller.Commands.Count);
        await ToggleAsync("ShowHintsCheckBox");
        Assert.Equal(Visibility.Visible, Control<TextBlock>("ControlUsageHint").Visibility);
        Assert.Equal(Visibility.Visible, Control<TextBox>("DeviceMemoryHint").Visibility);
        Assert.NotEmpty(AutomationProperties.GetHelpText(Control<Button>("SearchButton")));
        await ToggleAsync("ShowHintsCheckBox");
        using (JsonDocument saved = JsonDocument.Parse(File.ReadAllText(preferencesPath)))
        {
            Assert.False(saved.RootElement.GetProperty("ShowHints").GetBoolean());
            Assert.False(saved.RootElement.GetProperty("AutoApplySelections").GetBoolean());
        }
        window.CloseSectionWindows();

        foreach ((string choiceName, string buttonName) in new[]
        {
            ("InputChoice", "ApplyInputButton"),
            ("ModeChoice", "ApplyModeButton"),
            ("BrightnessChoice", "ApplyBrightnessButton")
        })
        {
            ComboBox choice = Control<ComboBox>(choiceName);
            foreach (ComboBoxItem item in choice.Items)
            {
                int previousCount = controller.Commands.Count;
                choice.SelectedItem = item;
                Assert.Equal(previousCount, controller.Commands.Count);
                await ClickAsync(buttonName);
                Assert.Equal(Assert.IsType<SoundbarCommand>(item.Tag), controller.Commands[^1]);
            }
        }

        foreach ((RoutedUICommand routed, SoundbarCommand expected) in new[]
        {
            (MainWindow.IncreaseSubwooferCommand, SoundbarCommand.SubwooferUp),
            (MainWindow.DecreaseSubwooferCommand, SoundbarCommand.SubwooferDown)
        })
        {
            Assert.True(routed.CanExecute(null, window));
            routed.Execute(null, window);
            await WaitForAsync(() => Control<Button>("RefreshButton").IsEnabled);
            Assert.Equal(expected, controller.Commands[^1]);
        }
        Assert.Equal(Enum.GetValues<SoundbarCommand>().Length, controller.Commands.Distinct().Count());

        foreach (int volume in new[] { 0, 50, 21 })
        {
            controller.PublishState(controller.State! with { Volume = volume });
            await WaitForAsync(() => Read("VolumeReadout") == volume.ToString());
            Assert.Equal(volume, controller.State!.Volume);
            Assert.Equal(volume.ToString(), Read("VolumeReadout"));
            Assert.Equal(volume < 50, MainWindow.IncreaseVolumeCommand.CanExecute(null, window));
            Assert.Equal(volume > 0, MainWindow.DecreaseVolumeCommand.CanExecute(null, window));
        }
        MainWindow.IncreaseVolumeCommand.Execute(null, window);
        await WaitForAsync(() => Control<Button>("RefreshButton").IsEnabled);
        Assert.Equal("22", Read("VolumeReadout"));
        MainWindow.DecreaseVolumeCommand.Execute(null, window);
        await WaitForAsync(() => Control<Button>("RefreshButton").IsEnabled);
        Assert.Equal("21", Read("VolumeReadout"));

        Assert.Null(window.FindName("VolumeTarget"));
        Assert.Null(window.FindName("ApplyVolumeButton"));
        int volumeRequests = controller.VolumeRequests;
        foreach (int invalid in new[] { -1, 51, int.MaxValue })
        {
            controller.PublishState(controller.State! with { Volume = invalid });
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.False(MainWindow.IncreaseVolumeCommand.CanExecute(null, window));
            Assert.False(MainWindow.DecreaseVolumeCommand.CanExecute(null, window));
            Assert.Equal(volumeRequests, controller.VolumeRequests);
        }
        controller.PublishState(controller.State! with { Volume = 21 });
        await WaitForAsync(() => Read("VolumeReadout") == "21");

        controller.DelayMutation = true;
        CheckBox muted = Control<CheckBox>("MuteToggle");
        bool? previousMute = muted.IsChecked;
        muted.IsChecked = !previousMute;
        await WaitForAsync(() => Control<Button>("CancelButton").IsEnabled);
        Assert.Equal(previousMute, muted.IsChecked);
        Assert.False(muted.IsEnabled);
        await ClickAsync("CancelButton");
        Assert.Null(muted.IsChecked);
        controller.DelayMutation = false;
        await ClickAsync("RefreshButton");
        Assert.Equal(previousMute, muted.IsChecked);

        await ToggleAsync("AutoApplySelectionsCheckBox");
        controller.FailNextMutation = true;
        modes.SelectedItem = modes.Items.Cast<ComboBoxItem>().First(item => !ReferenceEquals(item, modes.SelectedItem));
        await WaitForAsync(() => Control<Button>("RefreshButton").IsEnabled);
        Assert.Null(modes.SelectedItem);
        Assert.Null(Control<CheckBox>("PowerToggle").IsChecked);
        Assert.Contains("nie została potwierdzona", Read("StatusMessage"));
        await ClickAsync("RefreshButton");
        await ToggleAsync("AutoApplySelectionsCheckBox");

        controller.FailNextMutation = true;
        MainWindow.IncreaseVolumeCommand.Execute(null, window);
        await WaitForAsync(() => Control<Button>("RefreshButton").IsEnabled);
        Assert.Equal(21, controller.State!.Volume);
        Assert.Equal("Ostatnio: 21", Read("VolumeReadout"));
        Assert.False(MainWindow.IncreaseVolumeCommand.CanExecute(null, window));
        Assert.Contains("nie została potwierdzona", Read("StatusMessage"));
        await ClickAsync("RefreshButton");
        Assert.Equal("21", Read("VolumeReadout"));

        controller.DelayMutation = true;
        MainWindow.IncreaseVolumeCommand.Execute(null, window);
        await WaitForAsync(() => Control<Button>("CancelButton").IsEnabled);
        await ClickAsync("CancelButton");
        Assert.Equal(21, controller.State!.Volume);
        Assert.Contains("mogło już zostać wykonane", Read("StatusMessage"));
        controller.DelayMutation = false;
        await ClickAsync("RefreshButton");

        controller.DropConnection();
        await WaitForAsync(() => Control<Button>("SearchButton").IsEnabled);
        Assert.False(Control<CheckBox>("PowerToggle").IsEnabled);
        Assert.StartsWith("Ostatnio:", Read("VolumeReadout"));
        Assert.Equal("Ostatnio: SR-C30A; firmware: 1.23", Read("FirmwareDeviceReadout"));
        await ClickAsync("ConnectButton");
        Assert.Equal("20", Read("VolumeReadout"));
        await ClickAsync("DisconnectButton");
        controller.DelayDiscovery = true;
        Control<Button>("SearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForAsync(() => Control<Button>("CancelButton").IsEnabled);
        window.Close();
        await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        Assert.True(controller.DiscoveryCancelled);
        FakeSoundbarController idleController = new();
        MainWindow idleWindow = new(idleController, preferencesPath: preferencesPath)
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowState = WindowState.Minimized
        };
        idleWindow.Show();
        await idleWindow.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.False(Assert.IsType<CheckBox>(idleWindow.FindName("AutoApplySelectionsCheckBox")).IsChecked);
        Assert.False(Assert.IsType<CheckBox>(idleWindow.FindName("ShowHintsCheckBox")).IsChecked);
        Assert.Empty(AutomationProperties.GetHelpText(Assert.IsType<Button>(idleWindow.FindName("SearchButton"))));
        Assert.Empty(Assert.IsType<ListBox>(idleWindow.FindName("DevicesList")).Items.Cast<object>());
        Assert.False(idleController.IsConnected);
        idleWindow.Close();
        await WaitForAsync(() => idleController.Disposed && !idleWindow.IsVisible);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        do
        {
            await Task.Delay(10);
            if (condition())
                return;
        } while (DateTime.UtcNow < deadline);
        Assert.Fail("The expected WPF state was not reached before the deadline.");
    }

    private static async Task ExerciseSoundProfilesAsync()
    {
        foreach (string language in new[] { "pl", "en" })
        {
            string path = ConfigurationTests.TestPath("profile-flow-" + language + ".json");
            UiPreferences settings = new() { ConfirmExit = false, FirstRunCompleted = true, Language = language, Theme = "Dark", TextScale = 200 };
            Assert.True(settings.TrySave(out _, path));
            Localization.SetLanguage(language);
            FakeSoundbarController controller = new();
            bool approve = false;
            List<string> confirmations = [];
            MainWindow window = new(controller, null, path, new FakeStartupRegistration(), (message, _) =>
            {
                confirmations.Add(message);
                return approve;
            }) { ShowActivated = false, ShowInTaskbar = false, WindowState = WindowState.Minimized };
            window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            T Control<T>(string name) where T : FrameworkElement => Assert.IsType<T>(window.FindName(name));
            async Task ClickAsync(string name)
            {
                Button button = Control<Button>(name);
                Assert.True(button.IsEnabled, name);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            }
            Assert.False(Control<Button>("SaveSoundProfileButton").IsEnabled);
            await ClickAsync("SearchButton");
            AssertStableEntryIdentity(Control<ListBox>("DevicesList").Items[0]);
            Control<ListBox>("DevicesList").SelectedIndex = 0;
            Control<MenuItem>("ConnectOnceMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitForAsync(() => controller.IsConnected && !Control<Button>("CancelButton").IsEnabled);
            Assert.False(Control<Button>("SaveSoundProfileButton").IsEnabled);
            Assert.Empty(UiPreferences.Load(out _, path).SavedDevices);
            await ClickAsync("AddDeviceButton");
            SoundProfile expected = new(SoundMode.Game, true, true, 4);
            controller.PublishState(controller.State! with { Mode = expected.Mode, ClearVoice = true, BassExtension = true, Subwoofer = 4 });
            Control<TextBox>("SoundProfileNameInput").Text = "Test profile";
            await ClickAsync("SaveSoundProfileButton");
            Assert.Empty(controller.Commands);
            Assert.Equal(expected, Assert.Single(UiPreferences.Load(out _, path).SavedDevices).SoundProfile);
            Assert.Equal("Test profile", Assert.IsType<SavedSoundProfile>(Control<ListBox>("SoundProfilesList").SelectedItem).Name);
            string original = File.ReadAllText(path);
            await ClickAsync("SaveSoundProfileButton");
            Assert.Equal(original, File.ReadAllText(path));
            Assert.Single(confirmations);
            approve = true;
            using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.None))
                await ClickAsync("SaveSoundProfileButton");
            Assert.Equal(original, File.ReadAllText(path));
            await ClickAsync("RefreshButton");
            SoundbarState before = controller.State! with
            {
                Mode = SoundMode.Stereo, ClearVoice = false, BassExtension = false, Subwoofer = -4,
                Input = SoundbarInput.Analog, Volume = 7, Muted = true, Brightness = IndicatorBrightness.Off
            };
            controller.PublishState(before);
            approve = false;
            await ClickAsync("ApplySoundProfileButton");
            Assert.Empty(controller.Commands);
            approve = true;
            await ClickAsync("ApplySoundProfileButton");
            Assert.Equal(expected.Mode, controller.State!.Mode);
            Assert.Equal(expected.Subwoofer, controller.State.Subwoofer);
            Assert.True(controller.State.ClearVoice && controller.State.BassExtension);
            Assert.Equal(before.Input, controller.State.Input);
            Assert.Equal(before.Volume, controller.State.Volume);
            Assert.Equal(before.Muted, controller.State.Muted);
            Assert.Equal(before.Brightness, controller.State.Brightness);
            Assert.Equal(11, controller.Commands.Count);
            Assert.DoesNotContain(SoundbarCommand.PowerOn, controller.Commands);
            Assert.Equal(Localization.Text("Profile.Applied"), Control<TextBox>("StatusMessage").Text);
            controller.PublishState(controller.State with { Power = false });
            await WaitForAsync(() => Control<CheckBox>("PowerToggle").IsChecked == false);
            Assert.True(Control<Button>("ApplySoundProfileButton").IsEnabled);
            int standbyCommands = controller.Commands.Count;
            await ClickAsync("ApplySoundProfileButton");
            Assert.Equal(standbyCommands, controller.Commands.Count);
            Assert.Equal(Localization.Text("State.PowerOffEditBlocked"), Control<TextBox>("StatusMessage").Text);
            Assert.True(Control<Button>("SaveSoundProfileButton").IsEnabled);
            controller.PublishState(controller.State with { Power = true, Mode = SoundMode.Stereo });
            await WaitForAsync(() => Control<Button>("ApplySoundProfileButton").IsEnabled);
            controller.DelayMutation = true;
            Control<Button>("ApplySoundProfileButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => Control<Button>("CancelButton").IsEnabled);
            Assert.False(Control<Button>("SaveSoundProfileButton").IsEnabled);
            await ClickAsync("CancelButton");
            Assert.Contains(Localization.Text("Operation.UncertainEffect"), Control<TextBox>("StatusMessage").Text);
            Assert.False(Control<Button>("ApplySoundProfileButton").IsEnabled);
            controller.DelayMutation = false;
            await ClickAsync("RefreshButton");
            window.OpenSettingsWindow();
            Control<CheckBox>("ShowHintsCheckBox").IsChecked = false;
            Assert.True(window.CommitSettings());
            Assert.Equal(Visibility.Collapsed, Control<TextBlock>("SoundProfileHint").Visibility);
            int commandsBeforeReset = controller.Commands.Count;
            await ClickAsync("ResetApplicationButton");
            Assert.True(window.CommitSettings());
            Assert.Equal(commandsBeforeReset, controller.Commands.Count);
            Assert.Equal(expected, Assert.Single(UiPreferences.Load(out _, path).SavedDevices).SoundProfile);
            approve = false;
            await ClickAsync("DeleteSoundProfileButton");
            Assert.Equal(expected, Assert.Single(UiPreferences.Load(out _, path).SavedDevices).SoundProfile);
            approve = true;
            await ClickAsync("DeleteSoundProfileButton");
            Assert.Null(Assert.Single(UiPreferences.Load(out _, path).SavedDevices).SoundProfile);
            Assert.Equal(commandsBeforeReset, controller.Commands.Count);
            Assert.False(Control<Button>("ApplySoundProfileButton").IsEnabled);
            window.Close();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }

    private static async Task ExerciseDeviceStorageAsync()
    {
        string path = ConfigurationTests.TestPath("device-flow.json");
        UiPreferences initial = new() { ConfirmExit = false, FirstRunCompleted = true, Language = "en", Theme = "Light", ShowHints = true };
        Assert.True(initial.TrySave(out _, path));
        Localization.SetLanguage("en");
        FakeSoundbarController controller = new();
        MainWindow window = new(controller, preferencesPath: path)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowState = WindowState.Minimized
        };
        window.Show();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        T Control<T>(string name) where T : FrameworkElement => Assert.IsType<T>(window.FindName(name));
        async Task ClickAsync(string name)
        {
            Button button = Control<Button>(name);
            Assert.True(button.IsEnabled, name);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
        }
        Assert.Contains("Search", AutomationProperties.GetName(Control<Button>("SearchButton")));
        Assert.Empty(Control<ListBox>("SavedDevicesList").Items.Cast<object>());
        Assert.Empty(controller.ConnectionAddresses);
        await ClickAsync("SearchButton");
        string beforeOneOff = File.ReadAllText(path);
        MenuItem oneOff = Control<MenuItem>("ConnectOnceMenuItem");
        Assert.True(oneOff.IsEnabled);
        oneOff.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await WaitForAsync(() => Control<Button>("RefreshButton").IsEnabled);
        Assert.Equal(beforeOneOff, File.ReadAllText(path));
        Assert.Empty(Control<ListBox>("SavedDevicesList").Items.Cast<object>());
        await ClickAsync("AddDeviceButton");
        Assert.Single(UiPreferences.Load(out _, path).SavedDevices);
        Assert.Single(Control<ListBox>("SavedDevicesList").Items.Cast<object>());
        await ClickAsync("RemoveSavedDeviceButton");
        Assert.Empty(UiPreferences.Load(out _, path).SavedDevices);
        Assert.True(controller.IsConnected);
        Assert.Empty(controller.Commands);
        await ClickAsync("AddDeviceButton");
        await ClickAsync("DisconnectButton");
        Control<ListBox>("SavedDevicesList").SelectedIndex = 0;
        controller.ModelCode = 0x28;
        await ClickAsync("ConnectButton");
        Assert.False(controller.IsConnected);
        Assert.Single(UiPreferences.Load(out _, path).SavedDevices);
        controller.ModelCode = 0x27;
        await ClickAsync("ConnectButton");
        Assert.True(controller.IsConnected);
        Assert.Equal(3, controller.ConnectionAddresses.Count);
        Assert.All(controller.ConnectionAddresses, address => Assert.Equal(1UL, address));
        int writesBeforeLanguageChange = controller.Commands.Count;
        ComboBox languageChoice = Control<ComboBox>("SettingsLanguageChoice");
        languageChoice.SelectedItem = languageChoice.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == "pl");
        Assert.True(window.CommitSettings());
        Assert.Contains("Szuk", AutomationProperties.GetName(Control<Button>("SearchButton")));
        Assert.Equal("pl", UiPreferences.Load(out _, path).Language);
        Assert.Equal(writesBeforeLanguageChange, controller.Commands.Count);
        languageChoice.SelectedItem = languageChoice.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == "en");
        Assert.True(window.CommitSettings());
        Assert.Contains("Search", AutomationProperties.GetName(Control<Button>("SearchButton")));
        Assert.Contains("does not mean that 1.00 is the latest", Control<TextBox>("FirmwareGuideReadout").Text);
        Assert.Equal("en", UiPreferences.Load(out _, path).Language);
        ComboBox themeChoice = Control<ComboBox>("SettingsThemeChoice");
        themeChoice.SelectedItem = themeChoice.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == "Dark");
        Assert.True(window.CommitSettings());
        Assert.Equal("Dark", UiPreferences.Load(out _, path).Theme);
        Assert.Equal(writesBeforeLanguageChange, controller.Commands.Count);
        window.Close();
        await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        FakeSoundbarController reopenedController = new();
        MainWindow reopened = new(reopenedController, preferencesPath: path)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowState = WindowState.Minimized
        };
        reopened.Show();
        await reopened.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        ListBox savedDevices = Assert.IsType<ListBox>(reopened.FindName("SavedDevicesList"));
        string entry = Assert.Single(savedDevices.Items.Cast<object>()).ToString()!;
        Assert.Contains("00:00:00:00:00:01", entry);
        Assert.DoesNotContain("dBm", entry);
        Assert.Empty(reopenedController.ConnectionAddresses);
        savedDevices.SelectedIndex = 0;
        Assert.IsType<Button>(reopened.FindName("ConnectButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForAsync(() => reopenedController.IsConnected);
        Assert.Single(reopenedController.ConnectionAddresses);
        reopened.Close();
        await WaitForAsync(() => reopenedController.Disposed && !reopened.IsVisible);
    }

    private static async Task ExerciseTrayAsync()
    {
        string path = ConfigurationTests.TestPath("tray-flow.json");
        Assert.True(new UiPreferences { ConfirmExit = false, FirstRunCompleted = true, Language = "en", Theme = "Dark", ShowHints = false }.TrySave(out _, path));
        Localization.SetLanguage("en");
        FakeSoundbarController controller = new();
        MainWindow window = new(controller, preferencesPath: path)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowState = WindowState.Minimized
        };
        window.Show();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        T Control<T>(string name) where T : FrameworkElement => Assert.IsType<T>(window.FindName(name));
        async Task ClickAsync(string name)
        {
            Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
        }
        Assert.Null(window.Tray);
        Assert.False(window.HideForCloseShortcut());
        Assert.True(window.IsVisible);
        Control<CheckBox>("CloseToTrayCheckBox").IsChecked = true;
        Assert.True(window.CommitSettings());
        TrayController tray = Assert.IsType<TrayController>(window.Tray);
        Assert.True(tray.Visible);
        Assert.True(UiPreferences.Load(out _, path).CloseToTray);
        Assert.False(tray.CommandItem(SoundbarCommand.Game).IsEnabled);
        Control<ComboBox>("SettingsTextScaleChoice").SelectedValue = 200;
        Assert.True(window.CommitSettings());
        Assert.Equal(SystemFonts.MessageFontSize * 2, window.FontSize);
        Assert.Equal(window.FontSize, Control<Button>("SearchButton").FontSize);
        Assert.Equal(window.FontSize, Control<TextBox>("VolumeReadout").FontSize);
        Assert.Equal(200, UiPreferences.Load(out _, path).TextScale);
        await ClickAsync("SearchButton");
        await ClickAsync("ConnectButton");
        Assert.True(tray.CommandItem(SoundbarCommand.Standard).IsChecked);
        foreach (int invalidVolume in new[] { -1, 51, 255 })
        {
            tray.UpdateState(true, controller.State! with { Volume = invalidVolume }, false, false, false);
            Assert.False(tray.FindItem("Command.IncreaseVolume").IsEnabled);
            Assert.False(tray.FindItem("Command.DecreaseVolume").IsEnabled);
        }
        foreach (int invalidSubwoofer in new[] { -5, 5, 255 })
        {
            tray.UpdateState(true, controller.State! with { Subwoofer = invalidSubwoofer }, false, false, false);
            Assert.False(tray.CommandItem(SoundbarCommand.SubwooferUp).IsEnabled);
            Assert.False(tray.CommandItem(SoundbarCommand.SubwooferDown).IsEnabled);
        }
        tray.UpdateState(true, controller.State, false, false, false);
        MenuItem trayPower = tray.FindItem("Control.Power");
        Assert.True(trayPower.IsCheckable);
        AutomationPeer trayPowerPeer = UIElementAutomationPeer.CreatePeerForElement(trayPower)!;
        IToggleProvider trayPowerToggle = Assert.IsAssignableFrom<IToggleProvider>(trayPowerPeer.GetPattern(PatternInterface.Toggle));
        trayPowerToggle.Toggle();
        await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
        Assert.False(controller.State!.Power);
        Assert.False(trayPower.IsChecked);
        trayPowerToggle.Toggle();
        await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
        Assert.True(controller.State.Power);
        Assert.True(trayPower.IsChecked);
        controller.DelayMutation = true;
        trayPowerToggle.Toggle();
        await WaitForAsync(() => Control<Button>("CancelButton").IsEnabled);
        Assert.True(controller.State.Power);
        Assert.Equal(ToggleState.On, trayPowerToggle.ToggleState);
        Assert.Throws<ElementNotEnabledException>(() => trayPowerToggle.Toggle());
        await ClickAsync("CancelButton");
        Assert.Equal(ToggleState.Indeterminate, trayPowerToggle.ToggleState);
        controller.DelayMutation = false;
        await ClickAsync("RefreshButton");
        Assert.Equal(ToggleState.On, trayPowerToggle.ToggleState);
        Assert.True(window.HideForCloseShortcut());
        Assert.False(window.IsVisible);
        Assert.True(controller.IsConnected);
        Assert.False(controller.Disposed);
        Assert.True(tray.Visible);
        TrayController.InvokeItem(tray.CommandItem(SoundbarCommand.Game));
        await WaitForAsync(() => tray.CommandItem(SoundbarCommand.Game).IsEnabled);
        Assert.Equal(SoundbarCommand.Game, controller.Commands[^1]);
        Assert.True(tray.CommandItem(SoundbarCommand.Game).IsChecked);
        Assert.False(tray.CommandItem(SoundbarCommand.Standard).IsChecked);
        controller.DelayMutation = true;
        TrayController.InvokeItem(tray.CommandItem(SoundbarCommand.Movie3D));
        await WaitForAsync(() => Control<Button>("CancelButton").IsEnabled);
        Assert.False(tray.CommandItem(SoundbarCommand.Movie3D).IsEnabled);
        Assert.False(tray.CommandItem(SoundbarCommand.Movie3D).IsChecked);
        Assert.True(tray.CommandItem(SoundbarCommand.Game).IsChecked);
        TrayController.InvokeItem(tray.FindItem("Control.CancelName"));
        await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
        Assert.False(tray.CommandItem(SoundbarCommand.Game).IsEnabled);
        Assert.False(tray.CommandItem(SoundbarCommand.Game).IsChecked);
        controller.DelayMutation = false;
        TrayController.InvokeItem(tray.FindItem("Control.RefreshName"));
        await WaitForAsync(() => tray.CommandItem(SoundbarCommand.Game).IsEnabled);
        Assert.True(tray.CommandItem(SoundbarCommand.Game).IsChecked);
        window.RestoreFromTray();
        Assert.True(window.IsVisible);
        Assert.True(controller.IsConnected);
        int commands = controller.Commands.Count;
        Control<ComboBox>("SettingsLanguageChoice").SelectedIndex = 0;
        Assert.True(window.CommitSettings());
        Assert.Equal(Localization.Text("Command.Game"), tray.CommandItem(SoundbarCommand.Game).Header);
        Assert.Equal(commands, controller.Commands.Count);
        Control<CheckBox>("CloseToTrayCheckBox").IsChecked = false;
        Assert.True(window.CommitSettings());
        Assert.False(tray.Visible);
        Assert.Null(window.Tray);
        Assert.False(window.HideForCloseShortcut());
        Control<CheckBox>("CloseToTrayCheckBox").IsChecked = true;
        Assert.True(window.CommitSettings());
        TrayController finalTray = Assert.IsType<TrayController>(window.Tray);
        window.RequestExit();
        await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        Assert.False(finalTray.Visible);
        Assert.False(window.IsVisible);
    }

    private static async Task ExerciseStartupAndResetAsync()
    {
        string path = ConfigurationTests.TestPath("startup-window-flow.json");
        UiPreferences settings = new UiPreferences { ConfirmExit = false, FirstRunCompleted = true, Language = "en", Theme = "Dark" }
            .Remember(new("SR-C30A", "1.00", 0x27, 0), new("First soundbar", 1, 0))
            .Remember(new("SR-C30A", "1.00", 0x27, 0), new("Second soundbar", 2, 0));
        settings = settings with { AutoConnectAtStartup = false, StartupDeviceAddress = null };
        Assert.True(settings.TrySave(out _, path));
        FakeStartupRegistration registration = new();
        FakeSoundbarController firstController = new();
        MainWindow first = new(firstController, null, path, registration)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowState = WindowState.Minimized
        };
        first.Show();
        await first.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        T FirstControl<T>(string name) where T : FrameworkElement => Assert.IsType<T>(first.FindName(name));
        Assert.False(FirstControl<CheckBox>("PowerOnAtStartupCheckBox").IsEnabled);
        Assert.False(FirstControl<CheckBox>("StartWithWindowsCheckBox").IsChecked);
        SectionWindow firstSettings = first.OpenSettingsWindow();
        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            FirstControl<CheckBox>("StartWithWindowsCheckBox").IsChecked = true;
            Assert.False(registration.IsEnabled);
            Assert.False(first.CommitSettings());
            Assert.True(FirstControl<CheckBox>("StartWithWindowsCheckBox").IsChecked);
        }
        FirstControl<ComboBox>("StartupDeviceChoice").SelectedIndex = 1;
        FirstControl<CheckBox>("PowerOnAtStartupCheckBox").IsChecked = true;
        FirstControl<CheckBox>("StartWithWindowsCheckBox").IsChecked = true;
        Assert.True(first.CommitSettings());
        firstSettings.Close();
        Assert.True(registration.IsEnabled);
        Assert.True(UiPreferences.Load(out _, path).PowerOnAtStartup);
        Assert.Empty(firstController.Commands);
        Assert.Empty(firstController.ConnectionAddresses);
        first.Close();
        await WaitForAsync(() => firstController.Disposed && !first.IsVisible);

        bool confirm = false;
        FakeSoundbarController controller = new() { InitiallyPoweredOn = false };
        MainWindow window = new(controller, null, path, registration, (_, _) => confirm)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowState = WindowState.Minimized
        };
        window.Show();
        T Control<T>(string name) where T : FrameworkElement => Assert.IsType<T>(window.FindName(name));
        await WaitForAsync(() => controller.Commands.Count == 1 && !Control<Button>("CancelButton").IsEnabled);
        Assert.Equal(2UL, Assert.Single(controller.ConnectionAddresses));
        Assert.Equal(SoundbarCommand.PowerOn, Assert.Single(controller.Commands));
        Assert.True(controller.State!.Power);
        await window.RunStartupDeviceAsync();
        Assert.Single(controller.ConnectionAddresses);
        Assert.Single(controller.Commands);
        Control<CheckBox>("KeepPoweredOnCheckBox").IsChecked = true;
        Assert.True(window.CommitSettings());
        Assert.True(UiPreferences.Load(out _, path).KeepPoweredOn);
        byte[] previous = File.ReadAllBytes(path);
        Control<Button>("ResetApplicationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(previous, File.ReadAllBytes(path));
        Assert.True(UiPreferences.Load(out _, path).KeepPoweredOn);
        confirm = true;
        Control<Button>("ResetApplicationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(previous, File.ReadAllBytes(path));
        Assert.True(registration.IsEnabled);
        Assert.False(Control<CheckBox>("KeepPoweredOnCheckBox").IsChecked);
        Assert.True(window.CommitSettings());
        Assert.False(registration.IsEnabled);
        Assert.True(controller.IsConnected);
        Assert.Single(controller.Commands);
        UiPreferences reset = UiPreferences.Load(out _, path);
        Assert.Equal(2, reset.SavedDevices.Length);
        Assert.False(reset.CloseToTray);
        Assert.True(reset.ShowHints);
        Assert.True(reset.AutoApplySelections);
        Assert.Equal(100, reset.TextScale);
        Assert.False(reset.PowerOnAtStartup);
        Assert.False(reset.KeepPoweredOn);
        Assert.False(Control<CheckBox>("KeepPoweredOnCheckBox").IsChecked);
        Assert.False(reset.StartWithWindows);
        Assert.True(reset.IsSetupComplete);
        Assert.Equal("en", reset.Language);
        Assert.True(reset.ConfirmExit);
        Control<ComboBox>("StartupDeviceChoice").SelectedIndex = 0;
        Control<CheckBox>("PowerOnAtStartupCheckBox").IsChecked = true;
        Assert.True(window.CommitSettings());
        Control<ListBox>("SavedDevicesList").SelectedIndex = 0;
        Control<Button>("RemoveSavedDeviceButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(Control<CheckBox>("PowerOnAtStartupCheckBox").IsChecked);
        Assert.False(UiPreferences.Load(out _, path).PowerOnAtStartup);
        Assert.Null(UiPreferences.Load(out _, path).StartupDeviceAddress);
        window.Close();
        await WaitForAsync(() => controller.Disposed && !window.IsVisible);

        foreach (bool supportedModel in new[] { true, false })
        {
            Assert.True((settings with { PowerOnAtStartup = true, StartupDeviceAddress = 1 }).TrySave(out _, path));
            FakeSoundbarController automaticController = new() { ModelCode = supportedModel ? (byte)0x27 : (byte)0x99 };
            MainWindow automatic = new(automaticController, null, path, new FakeStartupRegistration())
            {
                ShowActivated = false, ShowInTaskbar = false, WindowState = WindowState.Minimized
            };
            automatic.Show();
            await automatic.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Single(automaticController.ConnectionAddresses);
            Assert.Equal(supportedModel, automaticController.IsConnected);
            Assert.Empty(automaticController.Commands);
            automatic.Close();
            await WaitForAsync(() => automaticController.Disposed && !automatic.IsVisible);
        }

        Assert.True(settings.TrySave(out _, path));
        FakeSoundbarController cancelController = new();
        MainWindow cancelledSetup = new(cancelController, null, path, new FakeStartupRegistration(), (_, _) => true)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowState = WindowState.Minimized
        };
        cancelledSetup.Show();
        SectionWindow cancelledSettings = cancelledSetup.OpenSettingsWindow();
        byte[] beforeReset = File.ReadAllBytes(path);
        Assert.IsType<Button>(cancelledSetup.FindName("ResetApplicationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(cancelledSetup.CancelSettings());
        cancelledSettings.Close();
        Assert.Equal(beforeReset, File.ReadAllBytes(path));
        Assert.True(UiPreferences.Load(out _, path).IsSetupComplete);
        cancelledSetup.Close();
        await WaitForAsync(() => cancelController.Disposed && !cancelledSetup.IsVisible);
        Assert.Equal(2, UiPreferences.Load(out _, path).SavedDevices.Length);
    }

    private static async Task ExerciseFeedbackAsync()
    {
        foreach (string language in new[] { "pl", "en" })
        {
            string path = ConfigurationTests.TestPath("feedback-" + language + ".json");
            Assert.True(new UiPreferences { ConfirmExit = false, FirstRunCompleted = true, Language = language, Theme = "Dark" }.TrySave(out _, path));
            Localization.SetLanguage(language);
            FakeSoundbarController controller = new();
            List<string> announcements = [];
            MainWindow window = new(controller, null, path, new FakeStartupRegistration(), announceState: announcements.Add)
            {
                ShowActivated = false, ShowInTaskbar = false, WindowState = WindowState.Minimized
            };
            window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            T Control<T>(string name) where T : FrameworkElement => Assert.IsType<T>(window.FindName(name));
            async Task ClickAsync(string name)
            {
                Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            }
            async Task AdjustAsync(RoutedUICommand command)
            {
                Assert.True(command.CanExecute(null, window));
                command.Execute(null, window);
                await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            }
            Assert.Equal(Visibility.Collapsed, Control<TextBox>("StatusMessage").Visibility);
            Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(Control<TextBox>("StatusMessage")));
            Assert.Equal(Visibility.Collapsed, Control<TextBox>("VolumeReadout").Visibility);
            Assert.Equal(Visibility.Visible, Control<TextBlock>("VolumeStaticReadout").Visibility);
            Assert.True(Control<TextBlock>("VolumeStaticReadout").Focusable);
            Assert.Equal(AutomationControlType.Text, UIElementAutomationPeer.CreatePeerForElement(Control<TextBlock>("VolumeStaticReadout"))!.GetAutomationControlType());
            await ClickAsync("SearchButton");
            Control<ListBox>("DevicesList").SelectedIndex = 0;
            await ClickAsync("ConnectButton");
            Assert.Empty(announcements);
            Assert.Equal(Localization.Text("Feedback.Volume", "20"), Control<TextBlock>("VolumeStaticReadout").Text);
            await AdjustAsync(MainWindow.DecreaseVolumeCommand);
            Assert.Equal(new[] { Localization.Text("Feedback.Volume", "19") }, announcements);
            await ClickAsync("RefreshButton");
            Assert.Single(announcements);
            await AdjustAsync(MainWindow.IncreaseSubwooferCommand);
            Assert.Equal(Localization.Text("Feedback.Subwoofer", "1"), announcements[^1]);
            Control<CheckBox>("PowerToggle").IsChecked = false;
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            Assert.Equal(Localization.Text("Feedback.Power", Localization.Text("Feedback.PowerOff")), announcements[^1]);
            Assert.Equal(3, announcements.Count);
            Control<CheckBox>("AnnounceStateChangesCheckBox").IsChecked = false;
            Assert.True(window.CommitSettings());
            await AdjustAsync(MainWindow.IncreaseVolumeCommand);
            Assert.Equal(3, announcements.Count);
            Assert.Equal(Localization.Text("State.PowerOffEditBlocked"), Control<TextBox>("StatusMessage").Text);
            Assert.Equal(19, controller.State!.Volume);
            Control<CheckBox>("PowerToggle").IsChecked = true;
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            await AdjustAsync(MainWindow.IncreaseVolumeCommand);
            Assert.Equal(20, controller.State.Volume);
            Assert.Equal(3, announcements.Count);
            Control<CheckBox>("AnnounceStateChangesCheckBox").IsChecked = true;
            Assert.True(window.CommitSettings());
            await ClickAsync("RefreshButton");
            Assert.Equal(3, announcements.Count);
            controller.FailNextMutation = true;
            controller.PublishStateBeforeMutationFailure = true;
            await AdjustAsync(MainWindow.IncreaseVolumeCommand);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            controller.PublishStateBeforeMutationFailure = false;
            Assert.Equal(3, announcements.Count);
            Assert.False(MainWindow.IncreaseVolumeCommand.CanExecute(null, window));
            Assert.Equal(Visibility.Visible, Control<TextBox>("StatusMessage").Visibility);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Control<TextBox>("StatusMessage")));
            Assert.Contains(Localization.Text("Readout.LastPrefix"), Control<TextBlock>("VolumeStaticReadout").Text);
            await ClickAsync("RefreshButton");
            Assert.Equal(3, announcements.Count);
            Assert.Equal(Visibility.Collapsed, Control<TextBox>("StatusMessage").Visibility);
            controller.DelayMutation = true;
            MainWindow.IncreaseVolumeCommand.Execute(null, window);
            await WaitForAsync(() => Control<Button>("CancelButton").IsEnabled);
            Assert.Equal(3, announcements.Count);
            Assert.Equal(Localization.Text("Feedback.Volume", "20"), Control<TextBlock>("VolumeStaticReadout").Text);
            await ClickAsync("CancelButton");
            Assert.Equal(3, announcements.Count);
            Assert.Equal(Visibility.Visible, Control<TextBox>("StatusMessage").Visibility);
            controller.DelayMutation = false;
            await ClickAsync("RefreshButton");
            Control<ComboBox>("ValueDisplayModeChoice").SelectedIndex = 0;
            Assert.True(window.CommitSettings());
            Assert.Equal(Visibility.Visible, Control<TextBox>("VolumeReadout").Visibility);
            Assert.Equal(Visibility.Collapsed, Control<TextBlock>("VolumeStaticReadout").Visibility);
            Assert.True(Control<TextBox>("VolumeReadout").IsReadOnly);
            Assert.True(Control<TextBox>("VolumeReadout").Focusable);
            Control<CheckBox>("ShowOperationMessagesCheckBox").IsChecked = true;
            Assert.True(window.CommitSettings());
            Assert.Equal(Visibility.Visible, Control<TextBox>("StatusMessage").Visibility);
            await AdjustAsync(MainWindow.DecreaseVolumeCommand);
            Assert.Equal(Localization.Text("Feedback.Volume", "19"), announcements[^1]);
            Control<CheckBox>("AnnounceStateChangesCheckBox").IsChecked = false;
            Assert.True(window.CommitSettings());
            UiPreferences saved = UiPreferences.Load(out _, path);
            Assert.True(saved.ShowOperationMessages);
            Assert.Equal(ValueDisplayMode.ReadOnlyFields, saved.ValueDisplayMode);
            Assert.False(saved.AnnounceStateChanges);
            Control<ComboBox>("ValueDisplayModeChoice").SelectedIndex = 1;
            Assert.True(window.CommitSettings());
            Assert.Equal(Visibility.Visible, Control<TextBlock>("PowerStaticReadout").Visibility);
            controller.DropConnection();
            await WaitForAsync(() => Control<Button>("SearchButton").IsEnabled);
            Assert.Contains(Localization.Text("Readout.LastPrefix"), Control<TextBlock>("VolumeStaticReadout").Text);
            Assert.Equal(4, announcements.Count);
            window.Close();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }

    private sealed class FakeStartupRegistration : IStartupRegistration
    {
        public bool IsEnabled { get; private set; }
        public List<bool> Changes { get; } = [];
        public void SetEnabled(bool enabled)
        {
            Changes.Add(enabled);
            IsEnabled = enabled;
        }
    }

    private sealed class TestApplication : Application
    {
        public Action? StartTest { get; set; }
        protected override void OnStartup(StartupEventArgs eventArgs) => StartTest?.Invoke();
    }
}
