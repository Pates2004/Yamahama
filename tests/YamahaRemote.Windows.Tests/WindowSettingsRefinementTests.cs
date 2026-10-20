using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static async Task ExerciseSettingsRefinementsAsync()
    {
        foreach (string language in new[] { "pl", "en" })
        foreach (string scope in new[] { "Appearance", "MainScreen", "Controls", "Startup", "Closing" })
            await ExerciseScopedSettingsResetAsync(language, scope);
    }

    private static UiPreferences RefinementPreferences(string language) => TransactionPreferences(language) with
    {
        TextScale = 150,
        HiddenRows = [ControlRow.Volume, ControlRow.Mute],
        ShowHints = false,
        AutoApplySelections = false,
        ShowOperationMessages = true,
        AnnounceStateChanges = false,
        ValueDisplayMode = ValueDisplayMode.ReadOnlyFields,
        VolumeControlMode = LevelControlMode.Buttons,
        SubwooferControlMode = LevelControlMode.Buttons,
        AutoConnectAtStartup = false,
        PowerOnAtStartup = true,
        StartWithWindows = true,
        KeepPoweredOn = true,
        CloseToTray = true,
        PowerOffOnExit = true,
        SavedDevices =
        [
            new("Connected fake device", 1)
            {
                SoundProfile = new(SoundMode.Game, true, true, 2),
                SavedSoundProfiles =
                [
                    new(Guid.NewGuid(), "Evening", new(SoundMode.Stereo, true, false, -2)),
                    new(Guid.NewGuid(), "Movies", new(SoundMode.Movie3D, false, true, 3))
                ]
            },
            new("Other saved device", 2)
            {
                SavedSoundProfiles = [new(Guid.NewGuid(), "Quiet", new(SoundMode.Standard, false, false, -4))]
            }
        ]
    };

    private static async Task ExerciseScopedSettingsResetAsync(string language, string scope)
    {
        string path = ConfigurationTests.TestPath($"settings-scoped-reset-{language}-{scope}.json");
        Localization.SetLanguage(language);
        Assert.True(RefinementPreferences(language).TrySave(out _, path));
        UiPreferences initial = LoadReadoutPreferences(path);
        Assert.Equal(3, initial.SavedDevices[0].SavedSoundProfiles.Length);
        FakeSoundbarController controller = new();
        FakeStartupRegistration registration = new();
        registration.SetEnabled(true);
        registration.Changes.Clear();
        bool approve = false;
        List<(string Message, string Title)> confirmations = [];
        MainWindow window = new(controller, null, path, registration, confirm: (message, title) =>
        {
            confirmations.Add((message, title));
            return approve;
        }) { ShowActivated = false, ShowInTaskbar = false };
        T Control<T>(string name) where T : FrameworkElement => ReadoutControl<T>(window, name);
        try
        {
            await ShowSettingsOwnerAsync(window, controller);
            SectionWindow settings = window.OpenSettingsWindow();
            await SettleReadoutWindowAsync(window);
            AssertRefinementSettingsLayout(window);
            AssertRefinementSettingsControls(window, initial);
            ListBox profiles = Control<ListBox>("SoundProfilesList");
            Assert.Equal(initial.SavedDevices[0].SavedSoundProfiles, profiles.Items.Cast<SavedSoundProfile>().ToArray());
            byte[] before = File.ReadAllBytes(path);
            UiPreferences draft = initial with
            {
                Language = language == "pl" ? "en" : "pl",
                Theme = "Light",
                HiddenRows = [ControlRow.Input, ControlRow.Volume, ControlRow.Mute]
            };
            void StageChanges()
            {
                ComboBox languages = Control<ComboBox>("SettingsLanguageChoice");
                languages.SelectedItem = languages.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, draft.Language));
                ComboBox themes = Control<ComboBox>("SettingsThemeChoice");
                themes.SelectedItem = themes.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, draft.Theme));
                Control<CheckBox>("ShowInputRowItem").IsChecked = false;
            }
            StageChanges();
            SelectSettingsTab(window, scope + "SettingsTab");
            Button reset = Control<Button>("Reset" + scope + "SettingsButton");
            Assert.Equal(scope, reset.Tag);
            Assert.True(reset.IsVisible && reset.IsEnabled && reset.IsTabStop);
            Assert.Equal(Localization.Text("Settings.ResetTab"), AutomationProperties.GetName(reset));
            settings.Activate();
            Assert.True(reset.Focus());
            AssertRefinementSettingsControls(window, draft);
            AssertRefinementLivePreferences(window, initial);

            void Reset(bool accepted)
            {
                approve = accepted;
                int previous = confirmations.Count;
                reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(previous + 1, confirmations.Count);
                Assert.Equal((Localization.Text("Settings.ResetTabConfirm", Localization.Text("Settings." + scope + "Tab")),
                    Localization.Text("Settings.ResetTabTitle")), confirmations[^1]);
            }

            Reset(false);
            AssertRefinementSettingsControls(window, draft);
            AssertRefinementLivePreferences(window, initial);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.True(registration.IsEnabled);
            Assert.Empty(registration.Changes);
            Assert.Equal(initial.SavedDevices[0].SavedSoundProfiles, profiles.Items.Cast<SavedSoundProfile>().ToArray());

            Reset(true);
            UiPreferences expected = ResetRefinementScope(draft, scope);
            AssertRefinementSettingsControls(window, expected);
            AssertRefinementLivePreferences(window, initial);
            Assert.Equal(before, File.ReadAllBytes(path));
            AssertSavedDevicesPreserved(initial.SavedDevices, LoadReadoutPreferences(path).SavedDevices);
            Assert.Equal(initial.SavedDevices[0].SavedSoundProfiles, profiles.Items.Cast<SavedSoundProfile>().ToArray());
            Assert.True(registration.IsEnabled);
            Assert.Empty(registration.Changes);
            Assert.Empty(controller.Commands);

            approve = false;
            ReadoutControl<Button>(settings, "SettingsCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(3, confirmations.Count);
            Assert.Equal((Localization.Text("Settings.DiscardConfirm"), Localization.Text("Settings.DiscardTitle")), confirmations[^1]);
            Assert.True(settings.IsVisible);
            AssertRefinementSettingsControls(window, expected);
            Assert.Equal(before, File.ReadAllBytes(path));
            approve = true;
            ReadoutControl<Button>(settings, "SettingsCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await SettleReadoutWindowAsync(window);
            Assert.Equal(4, confirmations.Count);
            Assert.False(settings.IsVisible);
            AssertRefinementLivePreferences(window, initial);
            Assert.Equal(before, File.ReadAllBytes(path));
            AssertSavedDevicesPreserved(initial.SavedDevices, LoadReadoutPreferences(path).SavedDevices);
            Assert.Empty(registration.Changes);

            settings = window.OpenSettingsWindow();
            await SettleReadoutWindowAsync(window);
            AssertRefinementSettingsControls(window, initial);
            StageChanges();
            SelectSettingsTab(window, scope + "SettingsTab");
            Reset(true);
            AssertRefinementSettingsControls(window, expected);
            Assert.Equal(before, File.ReadAllBytes(path));
            ReadoutControl<Button>(settings, "SettingsOkButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await SettleReadoutWindowAsync(window);
            Assert.False(settings.IsVisible);
            Assert.Equal(5, confirmations.Count);
            UiPreferences saved = LoadReadoutPreferences(path);
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(saved));
            AssertSavedDevicesPreserved(initial.SavedDevices, saved.SavedDevices);
            Assert.Equal(initial.SavedDevices[0].SavedSoundProfiles, profiles.Items.Cast<SavedSoundProfile>().ToArray());
            Assert.True(saved.IsSetupComplete);
            Assert.Equal(draft.Language, saved.Language);
            AssertRefinementLivePreferences(window, saved);
            Assert.Equal(expected.StartWithWindows, registration.IsEnabled);
            Assert.Equal(scope == "Startup" ? new[] { false } : Array.Empty<bool>(), registration.Changes);
            Assert.Empty(controller.Commands);
            settings = window.OpenSettingsWindow();
            await SettleReadoutWindowAsync(window);
            AssertRefinementSettingsControls(window, saved);
            settings.Close();
            Assert.False(settings.IsVisible);
            Assert.Equal(5, confirmations.Count);
        }
        finally
        {
            approve = true;
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }

    private static void AssertRefinementSettingsLayout(MainWindow window)
    {
        T Control<T>(string name) where T : FrameworkElement => ReadoutControl<T>(window, name);
        string[] scopes = ["Appearance", "MainScreen", "Controls", "Hotkeys", "Startup", "Closing", "Profiles"];
        Assert.Equal(scopes.Select(scope => scope + "SettingsTab"),
            Control<TabControl>("SettingsTabs").Items.Cast<TabItem>().Select(tab => tab.Name));
        foreach (string scope in scopes)
        {
            TabItem tab = Control<TabItem>(scope + "SettingsTab");
            string title = Localization.Text(scope == "Hotkeys" ? "Hotkeys.Tab" : "Settings." + scope + "Tab");
            Assert.Equal(title, AutomationProperties.GetName(tab));
            Assert.Equal(title, tab.Header);
            if (scope == "Profiles")
                continue;
            Button reset = Control<Button>("Reset" + scope + "SettingsButton");
            Assert.Equal(scope, reset.Tag);
            Assert.Equal(Localization.Text("Settings.ResetTab"), reset.Content);
            Assert.Contains(reset, RefinementLogicalDescendants(tab));
            Assert.True(reset.Focusable && reset.IsTabStop);
        }
        TabItem mainScreen = Control<TabItem>("MainScreenSettingsTab");
        foreach (ControlRow row in Enum.GetValues<ControlRow>())
            Assert.Contains(Control<CheckBox>(VisibilityToggleName(row)), RefinementLogicalDescendants(mainScreen));
        CheckBox powerOff = Control<CheckBox>("PowerOffOnExitCheckBox");
        Assert.Contains(powerOff, RefinementLogicalDescendants(Control<TabItem>("ClosingSettingsTab")));
        Assert.DoesNotContain(powerOff, RefinementLogicalDescendants(Control<TabItem>("StartupSettingsTab")));
        TabItem profiles = Control<TabItem>("ProfilesSettingsTab");
        Assert.Contains(Control<Button>("ResetApplicationButton"), RefinementLogicalDescendants(profiles));
        Assert.Contains(Control<ListBox>("SoundProfilesList"), RefinementLogicalDescendants(profiles));
        Assert.Equal(Localization.Text("Profile.Heading"), AutomationProperties.GetName(Control<ListBox>("SoundProfilesList")));
    }

    private static IEnumerable<DependencyObject> RefinementLogicalDescendants(DependencyObject parent)
    {
        foreach (DependencyObject child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (DependencyObject descendant in RefinementLogicalDescendants(child))
                yield return descendant;
        }
    }

    private static void AssertRefinementSettingsControls(MainWindow window, UiPreferences preferences)
    {
        T Control<T>(string name) where T : FrameworkElement => ReadoutControl<T>(window, name);
        (string Name, object? Value)[] choices =
        [
            ("SettingsLanguageChoice", preferences.Language), ("SettingsThemeChoice", preferences.Theme),
            ("ValueDisplayModeChoice", preferences.ValueDisplayMode),
            ("VolumeControlModeChoice", preferences.VolumeControlMode),
            ("SubwooferControlModeChoice", preferences.SubwooferControlMode)
        ];
        foreach (var choice in choices)
            Assert.Equal(choice.Value, Assert.IsType<ComboBoxItem>(Control<ComboBox>(choice.Name).SelectedItem).Tag);
        Assert.Equal(preferences.TextScale, Control<ComboBox>("SettingsTextScaleChoice").SelectedValue);
        int expectedTargetIndex = Array.FindIndex(preferences.SavedDevices, device => device.Address == preferences.StartupDeviceAddress);
        Assert.True(expectedTargetIndex == Control<ComboBox>("StartupDeviceChoice").SelectedIndex,
            $"Startup target: expected {expectedTargetIndex}, got {Control<ComboBox>("StartupDeviceChoice").SelectedIndex}; "
            + $"draft: {typeof(MainWindow).GetField("_settingsDraft", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)}");
        foreach (ControlRow row in Enum.GetValues<ControlRow>())
            Assert.Equal(!preferences.HiddenRows.Contains(row), Control<CheckBox>(VisibilityToggleName(row)).IsChecked);
        (string Name, bool Value)[] switches =
        [
            ("AutoApplySelectionsCheckBox", preferences.AutoApplySelections),
            ("ShowHintsCheckBox", preferences.ShowHints),
            ("ShowOperationMessagesCheckBox", preferences.ShowOperationMessages),
            ("AnnounceStateChangesCheckBox", preferences.AnnounceStateChanges),
            ("StartWithWindowsCheckBox", preferences.StartWithWindows),
            ("AutoConnectAtStartupCheckBox", preferences.AutoConnectAtStartup),
            ("PowerOnAtStartupCheckBox", preferences.PowerOnAtStartup),
            ("KeepPoweredOnCheckBox", preferences.KeepPoweredOn),
            ("CloseToTrayCheckBox", preferences.CloseToTray),
            ("ConfirmExitCheckBox", preferences.ConfirmExit),
            ("PowerOffOnExitCheckBox", preferences.PowerOffOnExit)
        ];
        foreach (var preference in switches)
            Assert.Equal(preference.Value, Control<CheckBox>(preference.Name).IsChecked);
    }

    private static string VisibilityToggleName(ControlRow row) => row switch
    {
        ControlRow.BassExtension => "ShowBassRowItem",
        ControlRow.IndicatorBrightness => "ShowBrightnessRowItem",
        _ => "Show" + row + "RowItem"
    };

    private static void AssertRefinementLivePreferences(MainWindow window, UiPreferences preferences)
    {
        Assert.Equal(preferences.Language, Localization.CurrentLanguage);
        Assert.Equal(SystemFonts.MessageFontSize * preferences.TextScale / 100.0, window.FontSize);
        Assert.Equal(preferences.ShowHints, UsageHints.GetEnabled(window));
#pragma warning disable WPF0001
        ThemeMode expected = preferences.Theme switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System
        };
        Assert.Equal(expected, Application.Current.ThemeMode);
#pragma warning restore WPF0001
        AssertReadoutPresentation(window, preferences.ValueDisplayMode, preferences.HiddenRows);
        if (preferences.CloseToTray)
            Assert.True(Assert.IsType<TrayController>(window.Tray).Visible);
        else
            Assert.Null(window.Tray);
    }

    private static void AssertSavedDevicesPreserved(SavedSoundbar[] expected, SavedSoundbar[] actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));

    private static UiPreferences ResetRefinementScope(UiPreferences preferences, string scope)
    {
        UiPreferences defaults = new();
        return scope switch
        {
            "Appearance" => preferences with { Theme = defaults.Theme, TextScale = defaults.TextScale },
            "MainScreen" => preferences with { HiddenRows = defaults.HiddenRows },
            "Controls" => preferences with
            {
                AutoApplySelections = defaults.AutoApplySelections,
                ShowHints = defaults.ShowHints,
                ShowOperationMessages = defaults.ShowOperationMessages,
                AnnounceStateChanges = defaults.AnnounceStateChanges,
                ValueDisplayMode = defaults.ValueDisplayMode,
                VolumeControlMode = defaults.VolumeControlMode,
                SubwooferControlMode = defaults.SubwooferControlMode
            },
            "Startup" => preferences with
            {
                StartupDeviceAddress = defaults.StartupDeviceAddress,
                AutoConnectAtStartup = defaults.AutoConnectAtStartup,
                PowerOnAtStartup = defaults.PowerOnAtStartup,
                StartWithWindows = defaults.StartWithWindows,
                KeepPoweredOn = defaults.KeepPoweredOn
            },
            "Closing" => preferences with
            {
                CloseToTray = defaults.CloseToTray,
                ConfirmExit = defaults.ConfirmExit,
                PowerOffOnExit = defaults.PowerOffOnExit
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
    }
}
