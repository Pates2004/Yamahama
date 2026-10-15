using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using YamahaRemote.Core;

namespace YamahaRemote.Windows;

public partial class MainWindow
{
    private readonly ObservableCollection<SavedSoundProfile> _soundProfileEntries = [];
    private readonly Dictionary<ulong, Guid?> _selectedSoundProfiles = [];
    private ulong? _profileDeviceAddress;
    private bool _updatingSoundProfiles;

    private SavedSoundbar? ConnectedSavedDevice => Connected && _identity?.ModelCode == 0x27 && _lastDevice is not null
        ? _uiPreferences.SavedDevices.FirstOrDefault(device => device.Address == _lastDevice.Address)
        : null;

    private SavedSoundProfile? SelectedSoundProfile => SoundProfilesList.SelectedItem is SavedSoundProfile selected
        ? ConnectedSavedDevice?.SavedSoundProfiles.FirstOrDefault(profile => profile.Id == selected.Id)
        : null;

    private void UpdateProfileControls(Guid? preferredProfileId = null)
    {
        SavedSoundbar? device = ConnectedSavedDevice;
        bool deviceChanged = _profileDeviceAddress != device?.Address;
        Guid? selectedId = (SoundProfilesList.SelectedItem as SavedSoundProfile)?.Id;
        int selectedIndex = SoundProfilesList.SelectedIndex;
        if (deviceChanged)
            selectedId = device is not null && _selectedSoundProfiles.TryGetValue(device.Address, out Guid? rememberedId)
                ? rememberedId : device?.SavedSoundProfiles.FirstOrDefault()?.Id;
        selectedId = preferredProfileId ?? selectedId;
        _updatingSoundProfiles = true;
        try
        {
            if (!ReferenceEquals(SoundProfilesList.ItemsSource, _soundProfileEntries))
                SoundProfilesList.ItemsSource = _soundProfileEntries;
            SavedSoundProfile[] profiles = device?.SavedSoundProfiles ?? [];
            if (deviceChanged)
                _soundProfileEntries.Clear();
            for (int index = _soundProfileEntries.Count - 1; index >= 0; index--)
            {
                if (!profiles.Any(profile => profile.Id == _soundProfileEntries[index].Id))
                    _soundProfileEntries.RemoveAt(index);
            }
            for (int index = 0; index < profiles.Length; index++)
            {
                SavedSoundProfile profile = profiles[index];
                int existingIndex = Enumerable.Range(0, _soundProfileEntries.Count)
                    .FirstOrDefault(entryIndex => _soundProfileEntries[entryIndex].Id == profile.Id, -1);
                if (existingIndex < 0)
                    _soundProfileEntries.Insert(index, profile);
                else if (existingIndex != index)
                    _soundProfileEntries.Move(existingIndex, index);
                if (_soundProfileEntries[index] != profile)
                    _soundProfileEntries[index] = profile;
            }
            SavedSoundProfile? selection = _soundProfileEntries.FirstOrDefault(profile => profile.Id == selectedId);
            if (selection is null && selectedId is not null && _soundProfileEntries.Count > 0)
                selection = _soundProfileEntries[Math.Clamp(selectedIndex, 0, _soundProfileEntries.Count - 1)];
            if (!ReferenceEquals(SoundProfilesList.SelectedItem, selection))
                SoundProfilesList.SelectedItem = selection;
            if (deviceChanged || preferredProfileId is not null || selection?.Id != selectedId)
                SoundProfileNameInput.Text = selection?.Name ?? string.Empty;
            _profileDeviceAddress = device?.Address;
            if (device is not null)
                _selectedSoundProfiles[device.Address] = selection?.Id;
        }
        finally
        {
            _updatingSoundProfiles = false;
        }
        SavedSoundProfile? selectedProfile = SelectedSoundProfile;
        SoundProfileNameInput.IsEnabled = CanOperate && device is not null;
        SoundProfilesList.IsEnabled = !_closing && device is not null;
        SaveSoundProfileButton.IsEnabled = CanAdjust && device is not null;
        ApplySoundProfileButton.IsEnabled = CanAdjust && selectedProfile is not null;
        DeleteSoundProfileButton.IsEnabled = CanOperate && selectedProfile is not null;
    }

    private void OnSoundProfileSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_initialized || _updatingSoundProfiles)
            return;
        SoundProfileNameInput.Text = SelectedSoundProfile?.Name ?? string.Empty;
        UpdateProfileControls();
    }

    private async void OnSaveSoundProfile(object sender, RoutedEventArgs eventArgs)
    {
        if (!CanAdjust || ConnectedSavedDevice is not SavedSoundbar device)
            return;
        if (RejectStandbyEdit())
            return;
        string name;
        try
        {
            name = SavedSoundProfile.NormalizeName(SoundProfileNameInput.Text);
        }
        catch (ArgumentException)
        {
            SetStatus(Localization.Text(string.IsNullOrWhiteSpace(SoundProfileNameInput.Text)
                ? "Profile.NameRequired" : "Profile.NameInvalid"), true);
            return;
        }
        SavedSoundProfile? existing = device.SavedSoundProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, name, StringComparison.Ordinal));
        if (existing is null && device.SavedSoundProfiles.Length >= SavedSoundbar.MaximumSoundProfiles)
        {
            SetStatus(Localization.Text("Profile.LimitReached"), true);
            return;
        }
        if (existing is not null && !_confirmSettingsAction(
            Localization.Text("Profile.OverwriteConfirm", name, device.Name), Localization.Text("Profile.Title")))
            return;
        Guid identifier = existing?.Id ?? Guid.NewGuid();
        await ExecuteAsync(Localization.Text("Profile.Capturing"), async token =>
        {
            EnsureProfileDevice(device.Address);
            SoundProfile profile = await SoundProfileOperations.CaptureAsync(_controller, token);
            token.ThrowIfCancellationRequested();
            CaptureFreshState(null);
            EnsureProfileDevice(device.Address);
            SavedSoundProfile saved = new(identifier, name, profile);
            SavedSoundProfile[] profiles = ConnectedSavedDevice!.SavedSoundProfiles;
            profiles = existing is null ? [.. profiles, saved]
                : profiles.Select(entry => entry.Id == identifier ? saved : entry).ToArray();
            SaveSoundProfiles(device.Address, profiles, identifier);
            return Localization.Text("Profile.Saved");
        }, requiresConnection: true, invalidateOnFailure: true, requiresPower: true);
    }

    private async void OnApplySoundProfile(object sender, RoutedEventArgs eventArgs)
    {
        if (!CanAdjust || ConnectedSavedDevice is not SavedSoundbar device || SelectedSoundProfile is not SavedSoundProfile profile)
            return;
        if (RejectStandbyEdit())
            return;
        if (!_confirmSettingsAction(Localization.Text("Profile.ApplyConfirm", profile.Name, device.Name), Localization.Text("Profile.Title")))
            return;
        await ExecuteAsync(Localization.Text("Profile.Applying"), async token =>
        {
            EnsureProfileDevice(device.Address);
            await SoundProfileOperations.ApplyAsync(_controller, profile.SoundProfile, token);
            token.ThrowIfCancellationRequested();
            CaptureFreshState(null);
            EnsureProfileDevice(device.Address);
            return Localization.Text("Profile.Applied");
        }, requiresConnection: true, changesState: true, invalidateOnFailure: true, requiresPower: true);
    }

    private void OnDeleteSoundProfile(object sender, RoutedEventArgs eventArgs)
    {
        if (!CanOperate || ConnectedSavedDevice is not SavedSoundbar device || SelectedSoundProfile is not SavedSoundProfile profile)
            return;
        bool deleteHadFocus = DeleteSoundProfileButton.IsKeyboardFocusWithin;
        if (!_confirmSettingsAction(Localization.Text("Profile.DeleteConfirm", profile.Name, device.Name), Localization.Text("Profile.Title")))
            return;
        try
        {
            EnsureProfileDevice(device.Address);
            SaveSoundProfiles(device.Address, ConnectedSavedDevice!.SavedSoundProfiles.Where(entry => entry.Id != profile.Id).ToArray());
            SetStatus(Localization.Text("Profile.Deleted"));
            if (deleteHadFocus && !DeleteSoundProfileButton.IsEnabled)
                SoundProfilesList.Focus();
        }
        catch (LocalizedOperationException exception)
        {
            SetStatus(DescribeError(exception), true);
        }
    }

    private void EnsureProfileDevice(ulong address)
    {
        if (ConnectedSavedDevice?.Address != address)
            throw new LocalizedOperationException("Profile.DeviceChanged");
    }

    private void SaveSoundProfiles(ulong address, SavedSoundProfile[] profiles, Guid? preferredProfileId = null)
    {
        UiPreferences candidate = _uiPreferences with
        {
            SavedDevices = _uiPreferences.SavedDevices.Select(device => device.Address == address
                ? device with { SavedSoundProfiles = profiles, LegacySoundProfile = null } : device).ToArray()
        };
        if (!candidate.TrySave(out _, _preferencesPath))
            throw new LocalizedOperationException("Configuration.SaveError");
        _uiPreferences = candidate;
        ReconcileSettingsDevices();
        UpdateProfileControls(preferredProfileId);
    }
}
