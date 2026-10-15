using System.Windows;
using System.Windows.Controls;
using YamahaRemote.Core;

namespace YamahaRemote.Windows;

public partial class MainWindow
{
    private readonly IStartupRegistration _startupRegistration;
    private readonly Func<string, string, bool> _confirmSettingsAction;
    private bool _startupAttempted;

    private void ApplyStartupPreferences()
    {
        bool wasApplying = _applyingPreferences;
        _applyingPreferences = true;
        try
        {
            try
            {
                bool registered = _startupRegistration.IsEnabled;
                StartWithWindowsCheckBox.IsChecked = registered;
                _uiPreferences = _uiPreferences with { StartWithWindows = registered };
            }
            catch (Exception exception)
            {
                StartWithWindowsCheckBox.IsChecked = false;
                _preferenceLoadWarning = Localization.Text("Startup.RegistrationFailed", DescribeError(exception, false));
            }
            PowerOnAtStartupCheckBox.IsChecked = _uiPreferences.PowerOnAtStartup;
            PowerOffOnExitCheckBox.IsChecked = _uiPreferences.PowerOffOnExit;
            RefreshStartupDevices();
        }
        finally
        {
            _applyingPreferences = wasApplying;
        }
    }

    private void RefreshStartupDevices()
    {
        ReconcileSettingsDevices();
        bool wasApplying = _applyingPreferences;
        _applyingPreferences = true;
        try
        {
            SavedDeviceEntry[] entries = _uiPreferences.SavedDevices.Select(device => new SavedDeviceEntry(device)).ToArray();
            StartupDeviceChoice.ItemsSource = entries;
            StartupDeviceChoice.SelectedItem = entries.FirstOrDefault(entry => entry.Device.Address == SettingsPreferences.StartupDeviceAddress);
            AutoConnectAtStartupCheckBox.IsChecked = SettingsPreferences.AutoConnectAtStartup;
            PowerOnAtStartupCheckBox.IsChecked = SettingsPreferences.PowerOnAtStartup;
            PowerOffOnExitCheckBox.IsChecked = SettingsPreferences.PowerOffOnExit;
        }
        finally
        {
            _applyingPreferences = wasApplying;
        }
    }

    private void OnStartupDeviceChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_initialized || _applyingPreferences || _closing)
            return;
        _settingsStartupTargetEdited = true;
        ulong? address = (StartupDeviceChoice.SelectedItem as SavedDeviceEntry)?.Device.Address;
        ChangeSettings(preferences => preferences with
        {
            StartupDeviceAddress = address,
            PowerOnAtStartup = address is not null && preferences.PowerOnAtStartup
        });
        PowerOnAtStartupCheckBox.IsChecked = SettingsPreferences.PowerOnAtStartup;
    }

    private void OnAutoConnectAtStartupChanged(object sender, RoutedEventArgs eventArgs)
    {
        ChangeSettings(preferences => preferences with { AutoConnectAtStartup = AutoConnectAtStartupCheckBox.IsChecked == true });
    }

    private void OnPowerOnAtStartupChanged(object sender, RoutedEventArgs eventArgs)
    {
        ChangeSettings(preferences => preferences with { PowerOnAtStartup = PowerOnAtStartupCheckBox.IsChecked == true });
    }

    private void OnPowerOffOnExitChanged(object sender, RoutedEventArgs eventArgs)
    {
        ChangeSettings(preferences => preferences with { PowerOffOnExit = PowerOffOnExitCheckBox.IsChecked == true });
    }

    private void OnStartWithWindowsChanged(object sender, RoutedEventArgs eventArgs)
    {
        ChangeSettings(preferences => preferences with { StartWithWindows = StartWithWindowsCheckBox.IsChecked == true });
    }

    private bool SaveWithStartupRegistration(UiPreferences candidate)
    {
        bool previous;
        try
        {
            previous = _startupRegistration.IsEnabled;
            if (previous != candidate.StartWithWindows)
                _startupRegistration.SetEnabled(candidate.StartWithWindows);
        }
        catch (Exception exception)
        {
            SetStatus(Localization.Text("Startup.RegistrationFailed", DescribeError(exception, false)), true);
            return false;
        }
        if (candidate.TrySave(out string? warning, _preferencesPath))
        {
            _uiPreferences = candidate;
            return true;
        }
        try
        {
            if (previous != candidate.StartWithWindows)
                _startupRegistration.SetEnabled(previous);
        }
        catch (Exception exception)
        {
            SetStatus(Localization.Text("Startup.RollbackFailed", DescribeError(exception, false)), true);
            return false;
        }
        SetStatus(warning ?? Localization.Text("Configuration.SaveError"), true);
        return false;
    }

    internal async Task RunStartupDeviceAsync()
    {
        if (_startupAttempted)
            return;
        _startupAttempted = true;
        if ((!_uiPreferences.AutoConnectAtStartup && !_uiPreferences.PowerOnAtStartup) || _closing || _busy)
            return;
        ulong? address = _uiPreferences.StartupDeviceAddress;
        if (address is null && !_uiPreferences.PowerOnAtStartup)
            return;
        SavedDeviceEntry? entry = SavedDevicesList.Items.Cast<SavedDeviceEntry>()
            .FirstOrDefault(item => item.Device.Address == address);
        if (entry is null)
        {
            SetStatus(Localization.Text("Startup.DeviceMissing"), true);
            return;
        }
        SavedDevicesList.SelectedItem = entry;
        await ConnectSelectedAsync(false);
        if (!_uiPreferences.PowerOnAtStartup || !CanAdjust || _lastDevice?.Address != address)
            return;
        if (_lastState!.Power)
            SetStatus(Localization.Text("Startup.AlreadyOn", entry.Device.Name));
        else
            await SendCommandAsync(SoundbarCommand.PowerOn);
    }

    private void OnResetApplication(object sender, RoutedEventArgs eventArgs)
    {
        if (_closing || _busy || !_confirmSettingsAction(Localization.Text("Reset.ApplicationConfirm"), Localization.Text("Reset.ApplicationTitle")))
            return;
        ChangeSettings(preferences => new UiPreferences
        {
            Language = preferences.Language ?? Localization.CurrentLanguage,
            FirstRunCompleted = preferences.FirstRunCompleted,
            SavedDevices = _uiPreferences.SavedDevices
        });
        _settingsDefaultsStaged = true;
        _settingsStartupTargetEdited = true;
        PopulateSettingsControls();
    }
}
