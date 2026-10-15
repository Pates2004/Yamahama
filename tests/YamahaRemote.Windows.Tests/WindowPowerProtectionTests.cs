using System.IO;
using System.Windows;
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
    private static async Task ExercisePowerProtectionAsync()
    {
        foreach (string language in new[] { "pl", "en" })
        {
            Localization.SetLanguage(language);
            string path = ConfigurationTests.TestPath($"protection-window-{language}.json");
            UiPreferences preferences = new()
            {
                ConfirmExit = false, FirstRunCompleted = true, Language = language, Theme = "Dark", CloseToTray = true,
                SavedDevices = [new("Simulated soundbar", 1)], StartupDeviceAddress = 1, AutoConnectAtStartup = true
            };
            Assert.True(preferences.TrySave(out _, path));
            ProtectionTestClock clock = new();
            FakeSoundbarController controller = new();
            MainWindow window = new(controller, null, path, new FakeStartupRegistration(), powerProtectionClock: clock)
            {
                ShowActivated = false, ShowInTaskbar = false
            };
            window.Show();
            await WaitForAsync(() => controller.IsConnected && window.IsLoaded);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            T Control<T>(string name) where T : FrameworkElement => Assert.IsType<T>(window.FindName(name));
            CheckBox enabled = Control<CheckBox>("KeepPoweredOnCheckBox");
            Assert.False(enabled.IsChecked);
            await window.CheckPowerProtectionAsync();
            Assert.Equal(0, controller.RefreshRequests);
            Assert.Empty(controller.Commands);
            SectionWindow settings = window.OpenSettingsWindow();
            SelectSettingsTab(window, "StartupSettingsTab");
            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(enabled)!;
            Assert.Equal(Localization.Text("PowerProtection.Enable"), peer.GetName());
            IToggleProvider toggle = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
            toggle.Toggle();
            Assert.True(window.CommitSettings());
            Assert.True(UiPreferences.Load(out _, path).KeepPoweredOn);
            using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                toggle.Toggle();
                Assert.False(window.CommitSettings());
                Assert.False(enabled.IsChecked);
                Assert.True(UiPreferences.Load(out _, path).KeepPoweredOn);
            }
            enabled.IsChecked = true;
            Control<CheckBox>("ShowHintsCheckBox").IsChecked = false;
            Assert.True(window.CommitSettings());
            Assert.Empty(peer.GetHelpText());
            Assert.Equal(Visibility.Visible, Control<TextBox>("PowerProtectionSafetyNotice").Visibility);
            await SettleReadoutWindowAsync(window);
            SelectSettingsTab(window, "StartupSettingsTab");
            settings.Activate();
            Assert.True(enabled.Focus(), $"Power protection focus: visible={enabled.IsVisible}, enabled={enabled.IsEnabled}, tab={((TabItem)Control<TabControl>("SettingsTabs").SelectedItem).Name}.");
            IInputElement? focus = Keyboard.FocusedElement;
            string status = Control<TextBox>("StatusMessage").Text;
            await window.CheckPowerProtectionAsync();
            Assert.Same(focus, Keyboard.FocusedElement);
            Assert.Equal(status, Control<TextBox>("StatusMessage").Text);
            Assert.Empty(controller.Commands);
            Assert.Equal(1, controller.RefreshRequests);

            controller.PublishState(controller.State! with { Power = false });
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(window.HideForCloseShortcut());
            await window.CheckPowerProtectionAsync();
            Assert.False(window.IsVisible);
            Assert.True(controller.State!.Power);
            Assert.Equal(new[] { SoundbarCommand.PowerOn }, controller.Commands);
            controller.PublishState(controller.State! with { Power = false });
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await window.CheckPowerProtectionAsync();
            Assert.Single(controller.Commands);

            clock.Advance(TimeSpan.FromSeconds(60));
            controller.FailNextMutation = true;
            controller.PublishState(controller.State! with { Power = false });
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await window.CheckPowerProtectionAsync();
            Assert.False(controller.State!.Power);
            Assert.Null(Control<CheckBox>("PowerToggle").IsChecked);
            Assert.Contains(Localization.Text("PowerProtection.Failed"), Control<TextBox>("StatusMessage").Text);
            Assert.False(window.IsVisible);
            int readsAfterFailure = controller.RefreshRequests;
            await window.CheckPowerProtectionAsync();
            Assert.Equal(readsAfterFailure, controller.RefreshRequests);
            Assert.Equal(2, controller.Commands.Count);
            clock.Advance(TimeSpan.FromSeconds(60));
            await window.CheckPowerProtectionAsync();
            Assert.True(controller.State!.Power);
            Assert.Equal(3, controller.Commands.Count);

            clock.Advance(TimeSpan.FromSeconds(60));
            controller.MutationGate = new();
            controller.PublishState(controller.State! with { Power = false });
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Task pending = window.CheckPowerProtectionAsync();
            Assert.False(pending.IsCompleted);
            enabled.IsChecked = false;
            Assert.True(window.CommitSettings());
            await pending;
            Assert.False(UiPreferences.Load(out _, path).KeepPoweredOn);
            Assert.Null(Control<CheckBox>("PowerToggle").IsChecked);
            controller.MutationGate = null;
            await window.CheckPowerProtectionAsync();
            Assert.Equal(4, controller.Commands.Count);
            enabled.IsChecked = true;
            Assert.True(window.CommitSettings());
            await window.CheckPowerProtectionAsync();
            Assert.False(controller.State!.Power);
            Assert.Equal(4, controller.Commands.Count);

            controller.PublishState(controller.State! with { Power = true });
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            clock.Advance(TimeSpan.FromSeconds(15));
            controller.PowerOnNextRefresh = false;
            await window.CheckPowerProtectionAsync();
            Assert.True(controller.State!.Power);
            Assert.Equal(5, controller.Commands.Count);
            controller.DropConnection();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            clock.Advance(TimeSpan.FromMinutes(5));
            int disconnectedReads = controller.RefreshRequests;
            await window.CheckPowerProtectionAsync();
            Assert.Equal(disconnectedReads, controller.RefreshRequests);
            Assert.Single(controller.ConnectionAddresses);
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
        await ExerciseProtectionSchedulingAsync();
        await ExerciseProtectionInterruptionAsync();
    }

    private static async Task ExerciseProtectionInterruptionAsync()
    {
        foreach (string interruption in new[] { "disconnect", "lost", "queued", "close" })
        {
            string path = ConfigurationTests.TestPath($"protection-interruption-{interruption}.json");
            Assert.True(new UiPreferences
            {
                ConfirmExit = false, FirstRunCompleted = true, Language = "en", Theme = "Light", KeepPoweredOn = true,
                SavedDevices = [new("Simulated soundbar", 1)], StartupDeviceAddress = 1, AutoConnectAtStartup = true
            }.TrySave(out _, path));
            List<string> shutdownWarnings = [];
            FakeSoundbarController controller = new() { MutationGate = new() };
            MainWindow window = new(controller, null, path, new FakeStartupRegistration(),
                showShutdownWarning: shutdownWarnings.Add)
            {
                ShowActivated = false, ShowInTaskbar = false
            };
            window.Show();
            await WaitForAsync(() => controller.IsConnected && window.IsLoaded);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            controller.PublishState(controller.State! with { Power = false });
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Task pending = window.CheckPowerProtectionAsync();
            Assert.False(pending.IsCompleted);
            Assert.Single(controller.Commands);
            if (interruption == "disconnect")
                ((Button)window.FindName("DisconnectButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else if (interruption is "lost" or "queued")
            {
                if (interruption == "queued")
                    ((CheckBox)window.FindName("MuteToggle")).IsChecked = true;
                controller.DropConnection();
            }
            else
                window.RequestExit();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, controller.CompletedMutations);
            if (interruption == "close")
            {
                await WaitForAsync(() => controller.Disposed && !window.IsVisible);
                Assert.Contains(Localization.Text("Operation.UncertainEffect"), Assert.Single(shutdownWarnings));
            }
            else
            {
                await WaitForAsync(() => !((Button)window.FindName("CancelButton")).IsEnabled && !controller.IsConnected);
                Assert.Contains(Localization.Text("Operation.UncertainEffect"), ((TextBox)window.FindName("StatusMessage")).Text);
                Assert.Equal(2, ((TextBox)window.FindName("StatusMessage")).Text.Split(
                    Localization.Text("Operation.UncertainEffect"), StringSplitOptions.None).Length);
                window.RequestExit();
                await WaitForAsync(() => controller.Disposed && !window.IsVisible);
                Assert.Empty(shutdownWarnings);
            }
        }
    }

    private static async Task ExerciseProtectionSchedulingAsync()
    {
        string path = ConfigurationTests.TestPath("protection-scheduling.json");
        Assert.True(new UiPreferences
        {
            ConfirmExit = false, FirstRunCompleted = true, Language = "en", Theme = "Light", KeepPoweredOn = true,
            SavedDevices = [new("Simulated soundbar", 1)], StartupDeviceAddress = 1, AutoConnectAtStartup = true
        }.TrySave(out _, path));
        ProtectionTestClock clock = new();
        FakeSoundbarController controller = new() { InitiallyPoweredOn = false };
        MainWindow window = new(controller, null, path, new FakeStartupRegistration(), powerProtectionClock: clock)
        {
            ShowActivated = false, ShowInTaskbar = false
        };
        window.Show();
        await WaitForAsync(() => controller.IsConnected && window.IsLoaded);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await window.CheckPowerProtectionAsync();
        Assert.False(controller.State!.Power);
        Assert.Empty(controller.Commands);
        controller.PublishState(controller.State! with { Power = true });
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        clock.Advance(TimeSpan.FromSeconds(15));
        controller.RefreshGate = new();
        controller.PowerOnNextRefresh = false;
        Task background = window.CheckPowerProtectionAsync();
        Assert.False(background.IsCompleted);
        ((CheckBox)window.FindName("MuteToggle")).IsChecked = true;
        Assert.True(((Button)window.FindName("CancelButton")).IsEnabled);
        await window.CheckPowerProtectionAsync();
        Assert.Empty(controller.Commands);
        controller.RefreshGate.SetResult();
        await background;
        await WaitForAsync(() => !((Button)window.FindName("CancelButton")).IsEnabled);
        Assert.Empty(controller.Commands);
        Assert.False(controller.State!.Muted);
        Assert.Equal(Localization.Text("State.PowerOffEditBlocked"), ((TextBox)window.FindName("StatusMessage")).Text);
        controller.RefreshGate = null;
        await window.CheckPowerProtectionAsync();
        Assert.Equal(new[] { SoundbarCommand.PowerOn }, controller.Commands);
        Assert.True(controller.State!.Power);
        ((CheckBox)window.FindName("MuteToggle")).IsChecked = true;
        await WaitForAsync(() => !((Button)window.FindName("CancelButton")).IsEnabled);
        Assert.Equal(new[] { SoundbarCommand.PowerOn, SoundbarCommand.MuteOn }, controller.Commands);

        clock.Advance(TimeSpan.FromSeconds(60));
        controller.RefreshWithoutNewState = true;
        controller.PublishState(controller.State! with { Power = false });
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await window.CheckPowerProtectionAsync();
        Assert.Equal(2, controller.Commands.Count);
        Assert.Null(((CheckBox)window.FindName("PowerToggle")).IsChecked);
        controller.RefreshWithoutNewState = false;
        clock.Advance(TimeSpan.FromSeconds(60));
        controller.IgnorePowerOn = true;
        await window.CheckPowerProtectionAsync();
        Assert.False(controller.State!.Power);
        Assert.Null(((CheckBox)window.FindName("PowerToggle")).IsChecked);
        Assert.Equal(3, controller.Commands.Count);
        controller.IgnorePowerOn = false;
        clock.Advance(TimeSpan.FromSeconds(60));
        window.Hide();
        for (int attempt = 0; attempt < 140 && !controller.State!.Power; attempt++)
            await Task.Delay(50);
        Assert.True(controller.State!.Power);
        Assert.False(window.IsVisible);
        Assert.Equal(4, controller.Commands.Count);

        clock.Advance(TimeSpan.FromSeconds(15));
        controller.RefreshGate = new();
        Task closingRead = window.CheckPowerProtectionAsync();
        Assert.False(closingRead.IsCompleted);
        window.RequestExit();
        await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        await closingRead;
        clock.Advance(TimeSpan.FromMinutes(5));
        await window.CheckPowerProtectionAsync();
        Assert.Equal(4, controller.Commands.Count);
    }
}
