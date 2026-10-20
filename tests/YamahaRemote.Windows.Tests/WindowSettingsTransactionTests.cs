using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static void SelectSettingsTab(MainWindow window, string name)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        TabControl tabs = Assert.IsType<TabControl>(window.FindName("SettingsTabs"));
        tabs.SelectedItem = Assert.IsType<TabItem>(window.FindName(name));
        Assert.IsType<SectionWindow>(Window.GetWindow(tabs)).UpdateLayout();
    }

    private static async Task ExerciseSettingsTransactionsAsync()
    {
        foreach (string language in new[] { "pl", "en" })
        {
            await ExerciseSettingsResetAsync(language);
            await ExerciseResetBeforeRememberingAsync(language, "Profiles");
            await ExerciseResetBeforeRememberingAsync(language, "Startup");
            await ExerciseSettingsIndependentChangesAsync(language);
            await ExerciseSettingsAcceptanceAsync(language);
            await ExerciseSettingsDismissalAsync(language);
        }
    }

    private static UiPreferences TransactionPreferences(string language) => new()
    {
        FirstRunCompleted = true, Language = language, Theme = "Dark", ConfirmExit = false,
        SavedDevices = [new("Connected fake device", 1), new("Other saved device", 2)],
        StartupDeviceAddress = 1, AutoConnectAtStartup = true, ShowHints = true
    };

    private static async Task ShowSettingsOwnerAsync(MainWindow window, FakeSoundbarController controller)
    {
        window.Show();
        await WaitForAsync(() => controller.IsConnected && window.IsLoaded
            && !ReadoutControl<Button>(window, "CancelButton").IsEnabled);
        await SettleReadoutWindowAsync(window);
        window.Activate();
        Assert.True(ReadoutControl<TextBlock>(window, "ReadoutStatus").Focus());
    }

    private static async Task ExerciseSettingsAcceptanceAsync(string language)
    {
        string path = ConfigurationTests.TestPath($"settings-transaction-{language}.json");
        UiPreferences initial = TransactionPreferences(language);
        Assert.True(initial.TrySave(out _, path));
        Localization.SetLanguage(language);
        FakeSoundbarController controller = new();
        FakeStartupRegistration registration = new();
        List<string> confirmations = [];
        MainWindow window = new(controller, null, path, registration, confirm: (message, _) =>
        {
            confirmations.Add(message);
            return true;
        }) { ShowActivated = false, ShowInTaskbar = false };
        T Control<T>(string name) where T : FrameworkElement => ReadoutControl<T>(window, name);
        try
        {
            await ShowSettingsOwnerAsync(window, controller);
            ListBox savedDevices = Control<ListBox>("SavedDevicesList");
            savedDevices.SelectedIndex = 1;
            object selectedDevice = savedDevices.SelectedItem;
            System.Collections.IEnumerable savedItems = savedDevices.ItemsSource;
            SectionWindow settings = window.OpenSettingsWindow();
            await SettleReadoutWindowAsync(window);
            Assert.Same(settings, window.OpenSettingsWindow());
            Assert.Equal(new[] { "AppearanceSettingsTab", "MainScreenSettingsTab", "ControlsSettingsTab", "HotkeysSettingsTab", "StartupSettingsTab", "ClosingSettingsTab", "ProfilesSettingsTab" },
                Control<TabControl>("SettingsTabs").Items.Cast<TabItem>().Select(tab => tab.Name));
            byte[] before = File.ReadAllBytes(path);
            double fontSize = window.FontSize;
#pragma warning disable WPF0001
            ThemeMode theme = Application.Current.ThemeMode;
#pragma warning restore WPF0001
            string nextLanguage = language == "pl" ? "en" : "pl";
            Control<ComboBox>("SettingsLanguageChoice").SelectedItem = Control<ComboBox>("SettingsLanguageChoice").Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, nextLanguage));
            Control<ComboBox>("SettingsThemeChoice").SelectedItem = Control<ComboBox>("SettingsThemeChoice").Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, "Light"));
            Control<ComboBox>("SettingsTextScaleChoice").SelectedValue = 150;
            Control<CheckBox>("ShowHintsCheckBox").IsChecked = false;
            Control<CheckBox>("ShowVolumeRowItem").IsChecked = false;
            Control<CheckBox>("AutoApplySelectionsCheckBox").IsChecked = false;
            Control<CheckBox>("ShowOperationMessagesCheckBox").IsChecked = true;
            Control<CheckBox>("AnnounceStateChangesCheckBox").IsChecked = false;
            Control<ComboBox>("ValueDisplayModeChoice").SelectedIndex = 2;
            Control<ComboBox>("VolumeControlModeChoice").SelectedIndex = 0;
            Control<ComboBox>("SubwooferControlModeChoice").SelectedIndex = 0;
            Control<CheckBox>("StartWithWindowsCheckBox").IsChecked = true;
            Control<CheckBox>("AutoConnectAtStartupCheckBox").IsChecked = false;
            Control<CheckBox>("PowerOnAtStartupCheckBox").IsChecked = true;
            Control<CheckBox>("PowerOffOnExitCheckBox").IsChecked = true;
            Control<CheckBox>("KeepPoweredOnCheckBox").IsChecked = true;
            Control<CheckBox>("CloseToTrayCheckBox").IsChecked = true;
            Control<CheckBox>("ConfirmExitCheckBox").IsChecked = true;
            await SettleReadoutWindowAsync(window);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(language, Localization.CurrentLanguage);
            Assert.Equal(fontSize, window.FontSize);
            Assert.Equal(fontSize, settings.FontSize);
#pragma warning disable WPF0001
            Assert.Equal(theme, Application.Current.ThemeMode);
#pragma warning restore WPF0001
            Assert.True(UsageHints.GetEnabled(window));
            Assert.Equal(Visibility.Visible, Control<ConfirmedSlider>("VolumeSlider").Visibility);
            AssertReadoutPresentation(window, ValueDisplayMode.StaticText);
            Assert.Null(window.Tray);
            Assert.False(registration.IsEnabled);
            Assert.Empty(registration.Changes);
            Assert.Empty(controller.Commands);
            using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ReadoutControl<Button>(settings, "SettingsOkButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await SettleReadoutWindowAsync(window);
                Assert.True(settings.IsVisible);
                Assert.Equal(before, File.ReadAllBytes(path));
                Assert.Equal(language, Localization.CurrentLanguage);
                Assert.Equal(fontSize, window.FontSize);
                Assert.Null(window.Tray);
                Assert.False(registration.IsEnabled);
                Assert.Equal(new[] { true, false }, registration.Changes);
                Assert.True(Control<CheckBox>("StartWithWindowsCheckBox").IsChecked);
                Assert.Equal(150, Control<ComboBox>("SettingsTextScaleChoice").SelectedValue);
                TextBox error = ReadoutControl<TextBox>(settings, "SectionStatusMessage");
                Assert.True(error.IsVisible);
                Assert.Contains(Localization.Text("Configuration.SaveError"), error.Text);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(error));
            }
            ReadoutControl<Button>(settings, "SettingsOkButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await SettleReadoutWindowAsync(window);
            Assert.False(settings.IsVisible);
            UiPreferences saved = LoadReadoutPreferences(path);
            Assert.Equal(nextLanguage, saved.Language);
            Assert.Equal("Light", saved.Theme);
            Assert.Equal(150, saved.TextScale);
            Assert.False(saved.ShowHints);
            Assert.Equal(new[] { ControlRow.Volume }, saved.HiddenRows);
            Assert.False(saved.AutoApplySelections);
            Assert.True(saved.ShowOperationMessages);
            Assert.False(saved.AnnounceStateChanges);
            Assert.Equal(ValueDisplayMode.None, saved.ValueDisplayMode);
            Assert.Equal(LevelControlMode.Buttons, saved.VolumeControlMode);
            Assert.Equal(LevelControlMode.Buttons, saved.SubwooferControlMode);
            Assert.True(saved.StartWithWindows && saved.PowerOnAtStartup && saved.PowerOffOnExit && saved.KeepPoweredOn);
            Assert.False(saved.AutoConnectAtStartup);
            Assert.True(saved.CloseToTray && saved.ConfirmExit);
            Assert.True(registration.IsEnabled);
            Assert.Equal(new[] { true, false, true }, registration.Changes);
            Assert.Equal(nextLanguage, Localization.CurrentLanguage);
            Assert.Same(savedItems, savedDevices.ItemsSource);
            Assert.Same(selectedDevice, savedDevices.SelectedItem);
            Assert.False(UsageHints.GetEnabled(window));
            Assert.Equal(SystemFonts.MessageFontSize * 1.5, window.FontSize);
            Assert.NotNull(window.Tray);
            AssertReadoutPresentation(window, ValueDisplayMode.None, ControlRow.Volume);
            Assert.Empty(confirmations);
            Assert.Empty(controller.Commands);
            Assert.Same(Control<TextBlock>("ReadoutStatus"), Keyboard.FocusedElement);
            settings = window.OpenSettingsWindow();
            await SettleReadoutWindowAsync(window);
            byte[] committed = File.ReadAllBytes(path);
            Assert.True(window.CommitSettings());
            Assert.True(settings.IsVisible);
            Assert.True(window.CancelSettings());
            settings.Close();
            Assert.Equal(committed, File.ReadAllBytes(path));
            Assert.Empty(confirmations);
        }
        finally
        {
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }

    private static async Task ExerciseSettingsDismissalAsync(string language)
    {
        string path = ConfigurationTests.TestPath($"settings-dismissal-{language}.json");
        Assert.True(TransactionPreferences(language).TrySave(out _, path));
        Localization.SetLanguage(language);
        bool approve = false;
        int confirmations = 0;
        FakeSoundbarController controller = new();
        MainWindow window = new(controller, null, path, new FakeStartupRegistration(), confirm: (message, title) =>
        {
            Assert.Equal(Localization.Text("Settings.DiscardConfirm"), message);
            Assert.Equal(Localization.Text("Settings.DiscardTitle"), title);
            confirmations++;
            return approve;
        }) { ShowActivated = false, ShowInTaskbar = false };
        try
        {
            await ShowSettingsOwnerAsync(window, controller);
            byte[] before = File.ReadAllBytes(path);
            foreach (string method in new[] { "cancel", "escape", "close" })
            {
                SectionWindow settings = window.OpenSettingsWindow();
                await SettleReadoutWindowAsync(window);
                SelectSettingsTab(window, "ControlsSettingsTab");
                CheckBox hints = ReadoutControl<CheckBox>(window, "ShowHintsCheckBox");
                hints.IsChecked = false;
                int previous = confirmations;
                approve = false;
                void Dismiss()
                {
                    if (method == "close")
                        settings.Close();
                    else if (method == "cancel")
                        ReadoutControl<Button>(settings, "SettingsCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else
                    {
                        settings.Activate();
                        Assert.True(hints.Focus());
                        PresentationSource source = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(settings));
                        KeyEventArgs escape = new(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent };
                        hints.RaiseEvent(escape);
                        Assert.True(escape.Handled);
                    }
                }
                Dismiss();
                await SettleReadoutWindowAsync(window);
                Assert.Equal(previous + 1, confirmations);
                Assert.True(settings.IsVisible);
                Assert.False(hints.IsChecked);
                Assert.True(UsageHints.GetEnabled(window));
                Assert.Equal(before, File.ReadAllBytes(path));
                Assert.False(window.CancelSettings());
                Assert.Equal(previous + 2, confirmations);
                approve = true;
                Dismiss();
                await SettleReadoutWindowAsync(window);
                Assert.Equal(previous + 3, confirmations);
                Assert.False(settings.IsVisible);
                Assert.Equal(before, File.ReadAllBytes(path));
                Assert.True(hints.IsChecked);
                Assert.Same(ReadoutControl<TextBlock>(window, "ReadoutStatus"), Keyboard.FocusedElement);
                Assert.False(ReadoutControl<MenuItem>(window, "FileMenu").IsKeyboardFocusWithin);
                settings = window.OpenSettingsWindow();
                await SettleReadoutWindowAsync(window);
                Assert.True(hints.IsChecked);
                hints.IsChecked = false;
                hints.IsChecked = true;
                settings.Close();
                Assert.False(settings.IsVisible);
                Assert.Equal(previous + 3, confirmations);
            }
            SectionWindow help = window.OpenHelpWindow();
            await SettleReadoutWindowAsync(window);
            help.Close();
            SectionWindow reopened = window.OpenSettingsWindow();
            await SettleReadoutWindowAsync(window);
            Assert.True(reopened.IsActive);
            Assert.True(ReadoutControl<ComboBox>(window, "SettingsLanguageChoice").IsKeyboardFocusWithin);
            reopened.Close();
            await SettleReadoutWindowAsync(window);
            Assert.Same(ReadoutControl<TextBlock>(window, "ReadoutStatus"), Keyboard.FocusedElement);
            Assert.Empty(controller.Commands);
        }
        finally
        {
            approve = true;
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }

    private static async Task ExerciseSettingsResetAsync(string language)
    {
        string path = ConfigurationTests.TestPath($"settings-reset-staging-{language}.json");
        SoundProfile profile = new(SoundMode.Game, true, true, 2);
        UiPreferences initial = TransactionPreferences(language) with
        {
            TextScale = 150, ShowHints = false, AutoApplySelections = false,
            SavedDevices = RefinementPreferences(language).SavedDevices
        };
        Assert.True(initial.TrySave(out _, path));
        Localization.SetLanguage(language);
        initial = LoadReadoutPreferences(path);
        bool approve = false;
        List<(string Message, string Title)> confirmations = [];
        FakeSoundbarController controller = new();
        MainWindow window = new(controller, null, path, new FakeStartupRegistration(), confirm: (message, title) =>
        {
            confirmations.Add((message, title));
            return approve;
        })
        { ShowActivated = false, ShowInTaskbar = false };
        try
        {
            await ShowSettingsOwnerAsync(window, controller);
            SectionWindow settings = window.OpenSettingsWindow();
            SelectSettingsTab(window, "ProfilesSettingsTab");
            ListBox profiles = ReadoutControl<ListBox>(window, "SoundProfilesList");
            SavedSoundProfile[] expectedProfiles = initial.SavedDevices[0].SavedSoundProfiles;
            Assert.Equal(expectedProfiles, profiles.Items.Cast<SavedSoundProfile>().ToArray());
            byte[] before = File.ReadAllBytes(path);
            ReadoutControl<Button>(window, "ResetApplicationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal((Localization.Text("Reset.ApplicationConfirm"), Localization.Text("Reset.ApplicationTitle")), Assert.Single(confirmations));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.False(ReadoutControl<CheckBox>(window, "ShowHintsCheckBox").IsChecked);
            Assert.False(ReadoutControl<CheckBox>(window, "ConfirmExitCheckBox").IsChecked);
            Assert.Equal(150, ReadoutControl<ComboBox>(window, "SettingsTextScaleChoice").SelectedValue);
            AssertRefinementSettingsControls(window, initial);
            Assert.Equal(expectedProfiles, profiles.Items.Cast<SavedSoundProfile>().ToArray());
            approve = true;
            ReadoutControl<Button>(window, "ResetApplicationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, confirmations.Count);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(SystemFonts.MessageFontSize * 1.5, window.FontSize);
            Assert.False(UsageHints.GetEnabled(window));
            Assert.True(ReadoutControl<CheckBox>(window, "ShowHintsCheckBox").IsChecked);
            Assert.True(ReadoutControl<CheckBox>(window, "ConfirmExitCheckBox").IsChecked);
            Assert.Equal(100, ReadoutControl<ComboBox>(window, "SettingsTextScaleChoice").SelectedValue);
            Assert.Equal(expectedProfiles, profiles.Items.Cast<SavedSoundProfile>().ToArray());
            Assert.Single(window.OwnedWindows.Cast<Window>());
            Assert.True(window.CancelSettings());
            settings.Close();
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.False(ReadoutControl<CheckBox>(window, "ShowHintsCheckBox").IsChecked);
            settings = window.OpenSettingsWindow();
            ReadoutControl<Button>(window, "ResetApplicationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.CommitSettings());
            Assert.True(settings.IsVisible);
            settings.Close();
            UiPreferences saved = LoadReadoutPreferences(path);
            Assert.True(saved.IsSetupComplete);
            Assert.Equal(language, saved.Language);
            AssertSavedDevicesPreserved(initial.SavedDevices, saved.SavedDevices);
            Assert.Contains(profile, saved.SavedDevices[0].SavedSoundProfiles.Select(savedProfile => savedProfile.SoundProfile));
            Assert.Equal(100, saved.TextScale);
            Assert.Equal("System", saved.Theme);
            Assert.True(saved.ShowHints && saved.AutoApplySelections && saved.AutoConnectAtStartup && saved.ConfirmExit);
            Assert.Null(saved.StartupDeviceAddress);
            Assert.False(saved.PowerOnAtStartup || saved.PowerOffOnExit || saved.KeepPoweredOn || saved.CloseToTray);
            Assert.Empty(window.OwnedWindows.Cast<Window>());
            Assert.Equal(expectedProfiles, profiles.Items.Cast<SavedSoundProfile>().ToArray());
            Assert.Empty(controller.Commands);
        }
        finally
        {
            approve = true;
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }

    private static async Task ExerciseResetBeforeRememberingAsync(string language, string scope)
    {
        string path = ConfigurationTests.TestPath($"settings-reset-before-remember-{language}-{scope}.json");
        UiPreferences initial = TransactionPreferences(language) with
        {
            SavedDevices = [], StartupDeviceAddress = null, AutoConnectAtStartup = false
        };
        Assert.True(initial.TrySave(out _, path));
        Localization.SetLanguage(language);
        FakeSoundbarController controller = new();
        MainWindow window = new(controller, null, path, new FakeStartupRegistration(), confirm: (_, _) => true)
        { ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            await SettleReadoutWindowAsync(window);
            ReadoutControl<Button>(window, "SearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => !ReadoutControl<Button>(window, "CancelButton").IsEnabled);
            ReadoutControl<MenuItem>(window, "ConnectOnceMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitForAsync(() => controller.IsConnected && !ReadoutControl<Button>(window, "CancelButton").IsEnabled);
            Assert.Empty(LoadReadoutPreferences(path).SavedDevices);
            SectionWindow settings = window.OpenSettingsWindow();
            SelectSettingsTab(window, scope + "SettingsTab");
            byte[] before = File.ReadAllBytes(path);
            string resetButton = scope == "Profiles" ? "ResetApplicationButton" : "ResetStartupSettingsButton";
            ReadoutControl<Button>(window, resetButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(-1, ReadoutControl<ComboBox>(window, "StartupDeviceChoice").SelectedIndex);
            Button remember = ReadoutControl<Button>(window, "AddDeviceButton");
            Assert.True(remember.IsEnabled);
            remember.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UiPreferences remembered = LoadReadoutPreferences(path);
            Assert.Equal(1UL, Assert.Single(remembered.SavedDevices).Address);
            Assert.Equal(1UL, remembered.StartupDeviceAddress);
            Assert.False(remembered.AutoConnectAtStartup);
            Assert.Equal(-1, ReadoutControl<ComboBox>(window, "StartupDeviceChoice").SelectedIndex);
            Assert.True(ReadoutControl<CheckBox>(window, "AutoConnectAtStartupCheckBox").IsChecked);
            Assert.True(window.CommitSettings());
            settings.Close();
            UiPreferences committed = LoadReadoutPreferences(path);
            AssertSavedDevicesPreserved(remembered.SavedDevices, committed.SavedDevices);
            Assert.Null(committed.StartupDeviceAddress);
            Assert.True(committed.AutoConnectAtStartup);
            Assert.True(committed.IsSetupComplete);
            Assert.Equal(language, committed.Language);
            Assert.False(committed.PowerOnAtStartup || committed.PowerOffOnExit);
            Assert.Empty(controller.Commands);
        }
        finally
        {
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }

    private static async Task ExerciseSettingsIndependentChangesAsync(string language)
    {
        foreach (bool commit in new[] { false, true })
        {
            string path = ConfigurationTests.TestPath($"settings-independent-{language}-{commit}.json");
            Assert.True(TransactionPreferences(language).TrySave(out _, path));
            Localization.SetLanguage(language);
            FakeSoundbarController controller = new();
            MainWindow window = new(controller, null, path, new FakeStartupRegistration(), confirm: (_, _) => true)
            { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                await ShowSettingsOwnerAsync(window, controller);
                SectionWindow settings = window.OpenSettingsWindow();
                SelectSettingsTab(window, "ProfilesSettingsTab");
                ListBox profiles = ReadoutControl<ListBox>(window, "SoundProfilesList");
                Assert.Empty(profiles.Items.Cast<object>());
                ReadoutControl<CheckBox>(window, "ShowHintsCheckBox").IsChecked = false;
                ReadoutControl<TextBox>(window, "SoundProfileNameInput").Text = "Transaction profile";
                ReadoutControl<Button>(window, "SaveSoundProfileButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForAsync(() => !ReadoutControl<Button>(window, "CancelButton").IsEnabled);
                SavedSoundProfile captured = Assert.Single(LoadReadoutPreferences(path).SavedDevices.Single(device => device.Address == 1).SavedSoundProfiles);
                Assert.Equal("Transaction profile", captured.Name);
                Assert.Single(profiles.Items.Cast<object>());
                ListBox devices = ReadoutControl<ListBox>(window, "SavedDevicesList");
                devices.SelectedIndex = 1;
                ReadoutControl<Button>(window, "RemoveSavedDeviceButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1UL, Assert.Single(LoadReadoutPreferences(path).SavedDevices).Address);
                Assert.True(LoadReadoutPreferences(path).ShowHints);
                if (commit)
                    Assert.True(window.CommitSettings());
                else
                    Assert.True(window.CancelSettings());
                settings.Close();
                UiPreferences saved = LoadReadoutPreferences(path);
                Assert.Equal(!commit, saved.ShowHints);
                Assert.Equal(1UL, Assert.Single(saved.SavedDevices).Address);
                Assert.Equal(captured, Assert.Single(Assert.Single(saved.SavedDevices).SavedSoundProfiles));
                Assert.Single(profiles.Items.Cast<object>());
                settings = window.OpenSettingsWindow();
                ReadoutControl<ComboBox>(window, "SettingsTextScaleChoice").SelectedValue = 125;
                devices.SelectedIndex = 0;
                ReadoutControl<Button>(window, "RemoveSavedDeviceButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(window.CommitSettings());
                saved = LoadReadoutPreferences(path);
                Assert.Empty(saved.SavedDevices);
                Assert.Empty(profiles.Items.Cast<object>());
                Assert.Null(saved.StartupDeviceAddress);
                Assert.False(saved.PowerOnAtStartup || saved.AutoConnectAtStartup);
                Assert.Equal(125, saved.TextScale);
                settings.Close();
                settings = window.OpenSettingsWindow();
                ReadoutControl<CheckBox>(window, "ShowHintsCheckBox").IsChecked = !saved.ShowHints;
                Button remember = ReadoutControl<Button>(window, "AddDeviceButton");
                Assert.True(remember.IsEnabled);
                remember.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                UiPreferences independentlyAdded = LoadReadoutPreferences(path);
                Assert.Equal(1UL, Assert.Single(independentlyAdded.SavedDevices).Address);
                Assert.Null(Assert.Single(independentlyAdded.SavedDevices).SoundProfile);
                Assert.Empty(Assert.Single(independentlyAdded.SavedDevices).SavedSoundProfiles);
                Assert.Equal(1UL, independentlyAdded.StartupDeviceAddress);
                Assert.True(window.CancelSettings());
                settings.Close();
                saved = LoadReadoutPreferences(path);
                AssertSavedDevicesPreserved(independentlyAdded.SavedDevices, saved.SavedDevices);
                Assert.Equal(independentlyAdded.ShowHints, saved.ShowHints);
                Assert.Equal(1UL, saved.StartupDeviceAddress);
                Assert.Empty(controller.Commands);
            }
            finally
            {
                window.RequestExit();
                await WaitForAsync(() => controller.Disposed && !window.IsVisible);
            }
        }
    }
}
