using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YamahaRemote.Core;

namespace YamahaRemote.Windows;

public partial class MainWindow
{
    private bool _updatingDeviceLists;

    private void OnDeviceListKeyboardFocus(object sender, KeyboardFocusChangedEventArgs eventArgs)
    {
        if (sender is not ListBox list)
            return;
        if (eventArgs.NewFocus is ListBoxItem item && ItemsControl.ItemsControlFromItemContainer(item) == list)
        {
            item.IsSelected = true;
            return;
        }
        if (eventArgs.NewFocus != list || !list.HasItems)
            return;
        object entry = list.SelectedItem ?? list.Items[0];
        list.ScrollIntoView(entry);
        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromItem(entry) is ListBoxItem target)
            target.Focus();
    }

    private DiscoveredSoundbar? SelectedDevice =>
        SavedDevicesList.SelectedItem is SavedDeviceEntry saved
            ? new DiscoveredSoundbar(saved.Device.Name, saved.Device.Address, 0)
            : (DevicesList.SelectedItem as DeviceEntry)?.Device;

    private async void OnConnectOnce(object sender, RoutedEventArgs eventArgs) => await ConnectSelectedAsync(false);

    private void RefreshSavedDevices(ulong? preferredAddress = null)
    {
        ulong? selectedAddress = preferredAddress ?? (SavedDevicesList.SelectedItem as SavedDeviceEntry)?.Device.Address;
        _updatingDeviceLists = true;
        try
        {
            SavedDeviceEntry[] entries = _uiPreferences.SavedDevices.Select(device => new SavedDeviceEntry(device)).ToArray();
            SavedDevicesList.ItemsSource = entries;
            SavedDevicesList.SelectedItem = entries.FirstOrDefault(entry => entry.Device.Address == selectedAddress);
            EmptySavedMessage.Visibility = entries.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (SavedDevicesList.SelectedItem is not null)
                DevicesList.SelectedItem = null;
        }
        finally
        {
            _updatingDeviceLists = false;
        }
        RefreshStartupDevices();
        UpdateAvailability();
    }

    private void OnSavedDeviceSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_initialized || _updatingDeviceLists)
            return;
        if (SavedDevicesList.SelectedItem is not null)
            DevicesList.SelectedItem = null;
        UpdateAvailability();
    }

    private string RememberConnectedDevice()
    {
        if (!Connected || _identity is null || _lastDevice is null || !_stateIsCurrent)
            return Localization.Text("Device.NotSaved");
        UiPreferences candidate;
        try
        {
            candidate = _uiPreferences.Remember(_identity, _lastDevice);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException)
        {
            return Localization.Text("Device.SaveFailed", Localization.Text("Device.InvalidName"));
        }
        if (!candidate.TrySave(out string? warning, _preferencesPath))
            return Localization.Text("Device.SaveFailed", warning ?? string.Empty);
        _uiPreferences = candidate;
        RefreshSavedDevices(_lastDevice.Address);
        return Localization.Text("Device.Saved");
    }

    private void OnAddDevice(object sender, RoutedEventArgs eventArgs)
    {
        if (!CanAdjust)
            return;
        SetStatus(RememberConnectedDevice());
    }

    private void OnRemoveSavedDevice(object sender, RoutedEventArgs eventArgs)
    {
        if (_closing || _busy || SavedDevicesList.SelectedItem is not SavedDeviceEntry selected)
            return;
        UiPreferences candidate = _uiPreferences.RemoveDevice(selected.Device.Address);
        if (!candidate.TrySave(out string? warning, _preferencesPath))
        {
            SetStatus(warning ?? Localization.Text("Configuration.SaveError"), true);
            return;
        }
        _uiPreferences = candidate;
        RefreshSavedDevices();
        SetStatus(Localization.Text("Device.Removed"));
        SavedDevicesList.Focus();
    }

    private void UpdateApplicationChoices()
    {
        bool previouslyApplying = _applyingPreferences;
        _applyingPreferences = true;
        try
        {
            SettingsLanguageChoice.SelectedItem = SettingsLanguageChoice.Items.Cast<ComboBoxItem>()
                .First(item => (string)item.Tag == (SettingsPreferences.Language ?? Localization.CurrentLanguage));
            SettingsThemeChoice.SelectedItem = SettingsThemeChoice.Items.Cast<ComboBoxItem>()
                .First(item => (string)item.Tag == SettingsPreferences.Theme);
        }
        finally
        {
            _applyingPreferences = previouslyApplying;
        }
    }

    private void OnSettingsLanguageChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (SettingsLanguageChoice.SelectedItem is ComboBoxItem { Tag: string language })
            ChangeSettings(preferences => preferences with { Language = language });
    }

    private void OnSettingsThemeChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (SettingsThemeChoice.SelectedItem is ComboBoxItem { Tag: string theme })
            ChangeSettings(preferences => preferences with { Theme = theme });
    }

    private sealed class SavedDeviceEntry(SavedSoundbar device) : INotifyPropertyChanged
    {
        public SavedSoundbar Device { get; } = device;
        public event PropertyChangedEventHandler? PropertyChanged;
        public string DisplayName => Localization.Text("Device.SavedEntry", Device.Name, FormatAddress(Device.Address));
        public void RefreshDisplayName() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        public override string ToString() => DisplayName;
    }
}
