using System.Collections;
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
    private static async Task ExerciseNamedSoundProfilesAsync()
    {
        foreach (string language in new[] { "pl", "en" })
            await ExerciseNamedSoundProfileLanguageAsync(language);
    }

    private static async Task ExerciseNamedSoundProfileLanguageAsync(string language)
    {
        string path = ConfigurationTests.TestPath($"named-profile-list-{language}.json");
        Assert.True(TransactionPreferences(language).TrySave(out _, path));
        Localization.SetLanguage(language);
        FakeSoundbarController controller = new();
        List<string> confirmations = [];
        bool approve = true;
        MainWindow window = new(controller, null, path, new FakeStartupRegistration(), confirm: (message, _) =>
        {
            confirmations.Add(message);
            return approve;
        }) { ShowActivated = false, ShowInTaskbar = false };
        T Control<T>(string name) where T : FrameworkElement => ReadoutControl<T>(window, name);
        async Task ClickAsync(string name)
        {
            Button button = Control<Button>(name);
            Assert.True(button.IsEnabled, name);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            await SettleReadoutWindowAsync(window);
        }
        SavedSoundProfile[] StoredProfiles() => LoadReadoutPreferences(path).SavedDevices.Single(device => device.Address == 1).SavedSoundProfiles;
        try
        {
            await ShowSettingsOwnerAsync(window, controller);
            ListBox devices = Control<ListBox>("SavedDevicesList");
            devices.SelectedIndex = 1;
            IEnumerable savedItems = devices.ItemsSource;
            object selectedDevice = devices.SelectedItem;
            SectionWindow settings = window.OpenSettingsWindow();
            SelectSettingsTab(window, "ProfilesSettingsTab");
            settings.Activate();
            await SettleReadoutWindowAsync(window);
            ListBox profiles = Control<ListBox>("SoundProfilesList");
            TextBox nameInput = Control<TextBox>("SoundProfileNameInput");
            IEnumerable profileItems = profiles.ItemsSource;
            Assert.Equal("Name", profiles.DisplayMemberPath);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(profiles)));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(nameInput)));
            Assert.True(profiles.IsTabStop && nameInput.IsTabStop);
            Assert.Empty(profiles.Items.Cast<object>());
            Assert.Equal(-1, profiles.SelectedIndex);
            Assert.Equal(string.Empty, nameInput.Text);
            Assert.False(Control<Button>("ApplySoundProfileButton").IsEnabled);
            Assert.False(Control<Button>("DeleteSoundProfileButton").IsEnabled);
            int initialRefreshes = controller.RefreshRequests;
            await ClickAsync("SaveSoundProfileButton");
            Assert.Equal(initialRefreshes, controller.RefreshRequests);
            Assert.Equal(Localization.Text("Profile.NameRequired"), Control<TextBox>("StatusMessage").Text);
            Assert.Empty(StoredProfiles());

            Control<CheckBox>("ShowHintsCheckBox").IsChecked = false;
            SoundProfile movieValues = new(SoundMode.Movie3D, true, true, 3);
            controller.PublishState(controller.State! with { Mode = movieValues.Mode, ClearVoice = true, BassExtension = true, Subwoofer = 3 });
            await SettleReadoutWindowAsync(window);
            nameInput.Text = "  Movie  ";
            await ClickAsync("SaveSoundProfileButton");
            SavedSoundProfile movie = Assert.Single(StoredProfiles());
            Assert.Equal("Movie", movie.Name);
            Assert.Equal(movieValues, movie.SoundProfile);
            Assert.Equal(movie.Id, Assert.IsType<SavedSoundProfile>(profiles.SelectedItem).Id);
            Assert.Equal(movie.Name, nameInput.Text);
            Assert.Empty(confirmations);
            Assert.Empty(controller.Commands);
            Assert.True(LoadReadoutPreferences(path).ShowHints);
            Assert.False(Control<CheckBox>("ShowHintsCheckBox").IsChecked);
            Assert.Same(savedItems, devices.ItemsSource);
            Assert.Same(selectedDevice, devices.SelectedItem);

            SoundProfile quietValues = new(SoundMode.Stereo, false, false, -2);
            controller.PublishState(controller.State! with { Mode = quietValues.Mode, ClearVoice = false, BassExtension = false, Subwoofer = -2 });
            await SettleReadoutWindowAsync(window);
            nameInput.Text = "Quiet";
            await ClickAsync("SaveSoundProfileButton");
            SavedSoundProfile quiet = Assert.Single(StoredProfiles(), profile => profile.Name == "Quiet");
            Assert.Equal(2, profiles.Items.Count);
            Assert.NotEqual(movie.Id, quiet.Id);
            Assert.Equal(quietValues, quiet.SoundProfile);
            Assert.Same(profileItems, profiles.ItemsSource);
            Assert.Empty(controller.Commands);
            Assert.Empty(LoadReadoutPreferences(path).SavedDevices.Single(device => device.Address == 2).SavedSoundProfiles);

            profiles.UpdateLayout();
            profiles.Focus();
            Assert.True(profiles.IsKeyboardFocusWithin);
            ListBoxItem quietItem = Assert.IsType<ListBoxItem>(profiles.ItemContainerGenerator.ContainerFromIndex(1));
            Assert.True(quietItem.Focus());
            PresentationSource source = PresentationSource.FromVisual(profiles)!;
            Assert.NotNull(source);
            quietItem.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Up)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            });
            await SettleReadoutWindowAsync(window);
            Assert.Equal(movie.Id, Assert.IsType<SavedSoundProfile>(profiles.SelectedItem).Id);
            Assert.Equal(movie.Name, nameInput.Text);
            Assert.True(profiles.IsKeyboardFocusWithin);
            Assert.True(((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            await SettleReadoutWindowAsync(window);
            Assert.True(nameInput.IsKeyboardFocusWithin,
                $"Profile Tab destination: {Keyboard.FocusedElement?.GetType().Name}/{(Keyboard.FocusedElement as FrameworkElement)?.Name}; section active={settings.IsActive}; input visible={nameInput.IsVisible}, enabled={nameInput.IsEnabled}; list tab stop={profiles.IsTabStop}; language={language}.");

            nameInput.Text = "Unfinished name";
            Assert.True(nameInput.Focus());
            IInputElement? nameFocus = Keyboard.FocusedElement;
            object selectedProfile = profiles.SelectedItem;
            object selectedContainer = profiles.ItemContainerGenerator.ContainerFromItem(selectedProfile);
            controller.PublishState(controller.State! with { Volume = 21 });
            await SettleReadoutWindowAsync(window);
            await ClickAsync("RefreshButton");
            Assert.Same(profileItems, profiles.ItemsSource);
            Assert.Same(selectedProfile, profiles.SelectedItem);
            Assert.Same(selectedContainer, profiles.ItemContainerGenerator.ContainerFromItem(selectedProfile));
            Assert.Equal("Unfinished name", nameInput.Text);
            Assert.Same(nameFocus, Keyboard.FocusedElement);
            Assert.Same(savedItems, devices.ItemsSource);
            Assert.Same(selectedDevice, devices.SelectedItem);

            nameInput.Text = "Movie";
            approve = false;
            int refreshesBeforeOverwrite = controller.RefreshRequests;
            string beforeOverwrite = File.ReadAllText(path);
            await ClickAsync("SaveSoundProfileButton");
            Assert.Equal(beforeOverwrite, File.ReadAllText(path));
            Assert.Equal(refreshesBeforeOverwrite, controller.RefreshRequests);
            Assert.Contains("Movie", confirmations[^1]);
            approve = true;
            await ClickAsync("SaveSoundProfileButton");
            SavedSoundProfile overwritten = Assert.Single(StoredProfiles(), profile => profile.Id == movie.Id);
            Assert.Equal(quietValues, overwritten.SoundProfile);
            Assert.Equal(2, StoredProfiles().Length);
            Assert.Empty(controller.Commands);
            Assert.Same(savedItems, devices.ItemsSource);
            Assert.Same(selectedDevice, devices.SelectedItem);

            profiles.SelectedItem = profiles.Items.Cast<SavedSoundProfile>().Single(profile => profile.Id == quiet.Id);
            SoundbarState beforeApply = controller.State! with
            {
                Mode = SoundMode.Game, ClearVoice = true, BassExtension = true, Subwoofer = 2,
                Input = SoundbarInput.Analog, Volume = 9, Muted = true, Brightness = IndicatorBrightness.Off
            };
            controller.PublishState(beforeApply);
            await SettleReadoutWindowAsync(window);
            approve = false;
            await ClickAsync("ApplySoundProfileButton");
            Assert.Empty(controller.Commands);
            approve = true;
            await ClickAsync("ApplySoundProfileButton");
            Assert.Equal(quietValues.Mode, controller.State!.Mode);
            Assert.Equal(quietValues.ClearVoice, controller.State.ClearVoice);
            Assert.Equal(quietValues.BassExtension, controller.State.BassExtension);
            Assert.Equal(quietValues.Subwoofer, controller.State.Subwoofer);
            Assert.Equal(beforeApply.Input, controller.State.Input);
            Assert.Equal(beforeApply.Volume, controller.State.Volume);
            Assert.Equal(beforeApply.Muted, controller.State.Muted);
            Assert.Equal(beforeApply.Brightness, controller.State.Brightness);
            Assert.Equal(quiet.Id, Assert.IsType<SavedSoundProfile>(profiles.SelectedItem).Id);
            Assert.Same(savedItems, devices.ItemsSource);
            Assert.Same(selectedDevice, devices.SelectedItem);

            int appliedCommands = controller.Commands.Count;
            approve = false;
            string beforeDelete = File.ReadAllText(path);
            await ClickAsync("DeleteSoundProfileButton");
            Assert.Equal(beforeDelete, File.ReadAllText(path));
            approve = true;
            Assert.True(nameInput.Focus());
            nameFocus = Keyboard.FocusedElement;
            await ClickAsync("DeleteSoundProfileButton");
            Assert.Equal(movie.Id, Assert.Single(StoredProfiles()).Id);
            Assert.Equal(movie.Id, Assert.IsType<SavedSoundProfile>(profiles.SelectedItem).Id);
            Assert.Same(nameFocus, Keyboard.FocusedElement);
            Assert.Same(profileItems, profiles.ItemsSource);
            await ClickAsync("DeleteSoundProfileButton");
            Assert.Empty(StoredProfiles());
            Assert.Empty(profiles.Items.Cast<object>());
            Assert.Equal(-1, profiles.SelectedIndex);
            Assert.Equal(string.Empty, nameInput.Text);
            Assert.False(Control<Button>("ApplySoundProfileButton").IsEnabled);
            Assert.False(Control<Button>("DeleteSoundProfileButton").IsEnabled);
            Assert.Same(nameFocus, Keyboard.FocusedElement);
            Assert.Same(savedItems, devices.ItemsSource);
            Assert.Same(selectedDevice, devices.SelectedItem);
            Assert.Equal(appliedCommands, controller.Commands.Count);
            nameInput.Text = "Last profile";
            await ClickAsync("SaveSoundProfileButton");
            Assert.Single(StoredProfiles());
            Assert.True(Control<Button>("DeleteSoundProfileButton").Focus());
            await ClickAsync("DeleteSoundProfileButton");
            Assert.Empty(StoredProfiles());
            Assert.Empty(profiles.Items.Cast<object>());
            Assert.True(profiles.IsKeyboardFocused);
            Assert.Equal(appliedCommands, controller.Commands.Count);
            Assert.True(window.CancelSettings());
            Assert.Empty(StoredProfiles());
            Assert.True(LoadReadoutPreferences(path).ShowHints);
            settings.Close();
        }
        finally
        {
            approve = true;
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }
}
