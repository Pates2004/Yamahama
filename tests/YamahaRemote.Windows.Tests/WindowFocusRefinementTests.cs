using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Xunit;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static async Task ExerciseFocusRefinementsAsync()
    {
        string path = ConfigurationTests.TestPath("focus-refinements.json");
        Assert.True(TransactionPreferences("pl").TrySave(out _, path));
        Localization.SetLanguage("pl");
        FakeSoundbarController controller = new();
        MainWindow window = new(controller, null, path, new FakeStartupRegistration(), confirm: (_, _) => true)
        { ShowActivated = false, ShowInTaskbar = false };
        T Control<T>(string name) where T : FrameworkElement => ReadoutControl<T>(window, name);
        try
        {
            await ShowSettingsOwnerAsync(window, controller);
            SectionWindow settings = window.OpenSettingsWindow();
            await SettleReadoutWindowAsync(window);
            settings.Activate();
            SelectSettingsTab(window, "StartupSettingsTab");
            ComboBox devices = Control<ComboBox>("StartupDeviceChoice");
            System.Collections.IEnumerable source = devices.ItemsSource;
            object secondDevice = devices.Items[1];
            AssertStableEntryIdentity(secondDevice);
            Assert.True(devices.Focus());
            Assert.False(Equals(devices.SelectedItem, secondDevice));
            Assert.True(devices.Items.Contains(secondDevice));
            int selectionChanges = 0;
            devices.SelectionChanged += (_, _) => selectionChanges++;
            devices.SelectedItem = secondDevice;
            Assert.True(ReferenceEquals(secondDevice, devices.SelectedItem), $"Selection was rejected synchronously; events={selectionChanges}; enabled={devices.IsEnabled}; current={devices.SelectedItem}; focus={Keyboard.FocusedElement}");
            await SettleReadoutWindowAsync(window);
            Assert.Same(source, devices.ItemsSource);
            Assert.Same(secondDevice, devices.SelectedItem);
            settings.Close();
            await SettleReadoutWindowAsync(window);

            foreach (string action in new[] { "ResetSoundButton", "FactoryResetGuideButton" })
            {
                Control<MenuItem>(action).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await SettleReadoutWindowAsync(window);
                TextBox guide = Control<TextBox>("ResetGuideReadout");
                Assert.True(guide.IsKeyboardFocused);
                Assert.IsType<SectionWindow>(Window.GetWindow(guide)).Close();
                await SettleReadoutWindowAsync(window);
            }

            foreach (bool fail in new[] { false, true })
            {
                window.Activate();
                Assert.True(Control<Button>("RefreshButton").Focus());
                controller.RefreshGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                controller.FailNextRefresh = fail;
                Control<Button>("RefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForAsync(() => Control<Button>("CancelButton").IsEnabled);
                window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Tab)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Assert.True(Control<TextBlock>("ConnectionSummary").Focus());
                controller.RefreshGate.SetResult();
                await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
                await SettleReadoutWindowAsync(window);
                Assert.True(Control<TextBlock>("ConnectionSummary").IsKeyboardFocused);
                controller.RefreshGate = null;
            }

            controller.RefreshGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Control<Button>("RefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => Control<Button>("CancelButton").IsEnabled);
            settings = window.OpenSettingsWindow();
            await SettleReadoutWindowAsync(window);
            settings.Activate();
            Assert.True(Control<TabItem>("AppearanceSettingsTab").Focus());
            foreach (string scope in new[] { "Appearance", "MainScreen", "Controls", "Startup", "Closing" })
                Assert.False(Control<Button>("Reset" + scope + "SettingsButton").IsEnabled);
            controller.RefreshGate.SetResult();
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            await SettleReadoutWindowAsync(window);
            Assert.True(Control<TabItem>("AppearanceSettingsTab").IsKeyboardFocused);
            foreach (string scope in new[] { "Appearance", "MainScreen", "Controls", "Startup", "Closing" })
                Assert.True(Control<Button>("Reset" + scope + "SettingsButton").IsEnabled);
            controller.RefreshGate = null;
            settings.Close();
            await SettleReadoutWindowAsync(window);
        }
        finally
        {
            controller.RefreshGate?.TrySetResult();
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }

    private static void AssertStableEntryIdentity(object entry)
    {
        System.ComponentModel.INotifyPropertyChanged notifications = Assert.IsAssignableFrom<System.ComponentModel.INotifyPropertyChanged>(entry);
        HashSet<object> entries = [entry];
        int hash = entry.GetHashCode();
        System.ComponentModel.PropertyChangedEventHandler handler = (_, _) => { };
        notifications.PropertyChanged += handler;
        try
        {
            Assert.Equal(hash, entry.GetHashCode());
            Assert.Contains(entry, entries);
        }
        finally
        {
            notifications.PropertyChanged -= handler;
        }
    }
}
