using System.Windows;
using System.Windows.Controls;

namespace YamahaRemote.Windows;

public partial class MainWindow
{
    private UiPreferences? _settingsDraft;
    private UiPreferences? _settingsBaseline;
    private bool _settingsStartupTargetEdited;
    private bool _settingsDefaultsStaged;

    private UiPreferences SettingsPreferences => _settingsDraft ?? _uiPreferences;

    private void BeginSettings()
    {
        _settingsBaseline ??= _uiPreferences;
        _settingsDraft ??= _uiPreferences;
        PopulateSettingsControls();
    }

    private void ChangeSettings(Func<UiPreferences, UiPreferences> change)
    {
        if (!_initialized || _applyingPreferences || _closing)
            return;
        _settingsBaseline ??= _uiPreferences;
        _settingsDraft = change(SettingsPreferences);
        UpdateAvailability();
    }

    internal bool CommitSettings()
    {
        if (_closing)
            return false;
        if (_busy)
        {
            SetStatus(Localization.Text("Settings.Busy"), true);
            return false;
        }
        if (_settingsDraft is null)
            return true;
        ReconcileSettingsDevices();
        UiPreferences candidate = _settingsDraft! with { SavedDevices = _uiPreferences.SavedDevices };
        bool previousAutoApply = _uiPreferences.AutoApplySelections;
        if (!SaveWithStartupRegistration(candidate))
            return false;
        _settingsDraft = null;
        _settingsBaseline = null;
        _settingsStartupTargetEdited = false;
        _settingsDefaultsStaged = false;
        if (previousAutoApply != candidate.AutoApplySelections)
            _manualSelectionDrafts.Clear();
        Localization.SetLanguage(candidate.Language ?? Localization.CurrentLanguage);
        ApplyTheme(candidate.Theme);
        ApplyControlRowVisibility();
        ApplyBehaviorPreferences();
        SynchronizeSettingControls();
        ApplyExtendedPreferences();
        EnsureTray();
        PopulateSettingsControls();
        UpdateAvailability();
        SetStatus(Localization.Text("Settings.Saved"));
        return true;
    }

    internal bool CancelSettings()
    {
        if (_settingsDraft is not null && _settingsBaseline is not null
            && !SameSettings(_settingsDraft, _settingsBaseline)
            && !_confirmSettingsAction(Localization.Text("Settings.DiscardConfirm"), Localization.Text("Settings.DiscardTitle")))
            return false;
        _settingsDraft = null;
        _settingsBaseline = null;
        _settingsStartupTargetEdited = false;
        _settingsDefaultsStaged = false;
        PopulateSettingsControls();
        UpdateAvailability();
        return true;
    }

    private static bool SameSettings(UiPreferences first, UiPreferences second)
    {
        return first.HiddenRows.ToHashSet().SetEquals(second.HiddenRows)
            && first with { SavedDevices = Array.Empty<SavedSoundbar>(), HiddenRows = Array.Empty<ControlRow>() }
                == second with { SavedDevices = Array.Empty<SavedSoundbar>(), HiddenRows = Array.Empty<ControlRow>() };
    }

    private void ReconcileSettingsDevices()
    {
        if (_settingsDraft is null || _settingsBaseline is null)
            return;
        ulong? target = _settingsStartupTargetEdited ? _settingsDraft.StartupDeviceAddress : _uiPreferences.StartupDeviceAddress;
        bool removed = target is not null && !_uiPreferences.SavedDevices.Any(device => device.Address == target);
        if (removed)
            target = null;
        bool autoConnect = !_settingsDefaultsStaged && _settingsDraft.AutoConnectAtStartup == _settingsBaseline.AutoConnectAtStartup
            ? _uiPreferences.AutoConnectAtStartup : _settingsDraft.AutoConnectAtStartup;
        bool powerOn = !_settingsDefaultsStaged && _settingsDraft.PowerOnAtStartup == _settingsBaseline.PowerOnAtStartup
            ? _uiPreferences.PowerOnAtStartup : _settingsDraft.PowerOnAtStartup;
        _settingsDraft = _settingsDraft with
        {
            SavedDevices = _uiPreferences.SavedDevices,
            StartupDeviceAddress = target,
            AutoConnectAtStartup = !removed && autoConnect,
            PowerOnAtStartup = target is not null && powerOn
        };
        _settingsBaseline = _settingsBaseline with
        {
            SavedDevices = _uiPreferences.SavedDevices,
            StartupDeviceAddress = _uiPreferences.StartupDeviceAddress,
            AutoConnectAtStartup = _uiPreferences.AutoConnectAtStartup,
            PowerOnAtStartup = _uiPreferences.PowerOnAtStartup
        };
    }

    private void PopulateSettingsControls()
    {
        bool wasApplying = _applyingPreferences;
        _applyingPreferences = true;
        try
        {
            UiPreferences preferences = SettingsPreferences;
            Select(SettingsLanguageChoice, preferences.Language ?? Localization.CurrentLanguage);
            Select(SettingsThemeChoice, preferences.Theme);
            SettingsTextScaleChoice.ItemsSource = TextScaling.Choices(preferences.TextScale);
            SettingsTextScaleChoice.SelectedValue = preferences.TextScale;
            foreach (var row in _controlRows)
                row.Toggle.IsChecked = !preferences.HiddenRows.Contains(row.Key);
            AutoApplySelectionsCheckBox.IsChecked = preferences.AutoApplySelections;
            ShowHintsCheckBox.IsChecked = preferences.ShowHints;
            ShowOperationMessagesCheckBox.IsChecked = preferences.ShowOperationMessages;
            AnnounceStateChangesCheckBox.IsChecked = preferences.AnnounceStateChanges;
            Select(ValueDisplayModeChoice, preferences.ValueDisplayMode);
            Select(VolumeControlModeChoice, preferences.VolumeControlMode);
            Select(SubwooferControlModeChoice, preferences.SubwooferControlMode);
            CloseToTrayCheckBox.IsChecked = preferences.CloseToTray;
            ConfirmExitCheckBox.IsChecked = preferences.ConfirmExit;
            StartWithWindowsCheckBox.IsChecked = preferences.StartWithWindows;
            KeepPoweredOnCheckBox.IsChecked = preferences.KeepPoweredOn;
            RefreshStartupDevices();
        }
        finally
        {
            _applyingPreferences = wasApplying;
        }

        static void Select(ComboBox control, object value) => control.SelectedItem = control.Items.Cast<ComboBoxItem>()
            .First(item => Equals(item.Tag, value));
    }

    private void OnConfirmExitChanged(object sender, RoutedEventArgs eventArgs) =>
        ChangeSettings(preferences => preferences with { ConfirmExit = ConfirmExitCheckBox.IsChecked == true });

    private void OnResetSettingsTab(object sender, RoutedEventArgs eventArgs)
    {
        if (!_initialized || _closing || _busy || sender is not Button { Tag: string scope })
            return;
        string titleKey = scope switch
        {
            "Appearance" => "Settings.AppearanceTab",
            "MainScreen" => "Settings.MainScreenTab",
            "Controls" => "Settings.ControlsTab",
            "Startup" => "Settings.StartupTab",
            "Closing" => "Settings.ClosingTab",
            _ => throw new ArgumentOutOfRangeException(nameof(sender))
        };
        if (!_confirmSettingsAction(Localization.Text("Settings.ResetTabConfirm", Localization.Text(titleKey)),
            Localization.Text("Settings.ResetTabTitle")))
            return;
        UiPreferences defaults = new();
        ChangeSettings(preferences => scope switch
        {
            "Appearance" => preferences with { Theme = defaults.Theme, TextScale = defaults.TextScale },
            "MainScreen" => preferences with { HiddenRows = defaults.HiddenRows },
            "Controls" => preferences with
            {
                VolumeControlMode = defaults.VolumeControlMode,
                SubwooferControlMode = defaults.SubwooferControlMode,
                AutoApplySelections = defaults.AutoApplySelections,
                ShowHints = defaults.ShowHints,
                ShowOperationMessages = defaults.ShowOperationMessages,
                ValueDisplayMode = defaults.ValueDisplayMode,
                AnnounceStateChanges = defaults.AnnounceStateChanges
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
            _ => preferences
        });
        if (scope == "Startup")
        {
            _settingsDefaultsStaged = true;
            _settingsStartupTargetEdited = true;
        }
        PopulateSettingsControls();
    }
}
