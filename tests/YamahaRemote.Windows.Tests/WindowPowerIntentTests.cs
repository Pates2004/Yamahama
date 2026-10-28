using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;
using YamahaRemote.Core;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static async Task ExercisePowerIntentAsync()
    {
        foreach (string language in new[] { "pl", "en" })
        foreach (string route in new[] { "Main", "Tray", "Hotkey" })
        {
            Localization.SetLanguage(language);
            string path = ConfigurationTests.TestPath($"manual-power-{language}-{route}.json");
            FakeSoundbarController controller = new();
            ProtectionTestClock clock = new();
            MainWindow window = await CreatePowerIntentWindowAsync(path, controller, clock);
            try
            {
                if (route == "Main")
                    ((CheckBox)window.FindName("PowerToggle")).IsChecked = false;
                else if (route == "Tray")
                    TrayController.InvokeItem(window.Tray!.FindItem("Control.Power"));
                else
                    await window.ExecuteGlobalHotkeyAsync(HotkeyAction.TogglePower);
                await WaitForAsync(() => !((Button)window.FindName("CancelButton")).IsEnabled);
                Assert.Equal(new[] { SoundbarCommand.PowerOff }, controller.Commands);
                Assert.False(controller.State!.Power);
                Assert.True(UiPreferences.Load(out _, path).KeepPoweredOn);
                Assert.True(window.HideForCloseShortcut());
                string status = ((TextBox)window.FindName("StatusMessage")).Text;
                int reads = controller.RefreshRequests;
                controller.FailNextRefresh = true;
                for (int check = 0; check < 3; check++)
                {
                    clock.Advance(TimeSpan.FromMinutes(2));
                    await window.CheckPowerProtectionAsync();
                }
                Assert.Equal(reads, controller.RefreshRequests);
                Assert.Single(controller.Commands);
                Assert.Equal(status, ((TextBox)window.FindName("StatusMessage")).Text);
                Assert.False(window.IsVisible);
                controller.FailNextRefresh = false;
                controller.PublishState(controller.State with { Power = true });
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                controller.PublishState(controller.State! with { Power = false });
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await window.CheckPowerProtectionAsync();
                Assert.Equal(new[] { SoundbarCommand.PowerOff, SoundbarCommand.PowerOn }, controller.Commands);
                Assert.True(controller.State!.Power);
                Assert.False(window.IsVisible);
            }
            finally
            {
                window.RequestExit();
                await WaitForAsync(() => controller.Disposed && !window.IsVisible);
            }
        }

        foreach (string outcome in new[] { "Failure", "Cancellation", "StillOn", "Ignored" })
        {
            string path = ConfigurationTests.TestPath($"manual-power-{outcome}.json");
            FakeSoundbarController controller = new() { MutationGate = new() };
            ProtectionTestClock clock = new();
            MainWindow window = await CreatePowerIntentWindowAsync(path, controller, clock);
            try
            {
                Task pending = window.ExecuteGlobalHotkeyAsync(HotkeyAction.TogglePower);
                Assert.False(pending.IsCompleted);
                controller.PublishState(controller.State! with { Power = true });
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (outcome == "Cancellation")
                    ((Button)window.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else
                {
                    controller.FailNextMutation = outcome != "Ignored";
                    controller.IgnorePowerOff = outcome == "Ignored";
                    controller.MutationGate.SetResult();
                }
                await pending;
                controller.MutationGate = null;
                Assert.Single(controller.Commands);
                Assert.Contains(Localization.Text("Operation.UncertainEffect"), ((TextBox)window.FindName("StatusMessage")).Text);
                clock.Advance(TimeSpan.FromMinutes(2));
                await window.CheckPowerProtectionAsync();
                Assert.Equal(0, controller.RefreshRequests);
                bool remainedOn = outcome is "StillOn" or "Ignored";
                SoundbarState next = controller.State! with { Power = remainedOn };
                controller.PublishState(next);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (remainedOn)
                {
                    controller.PublishState(controller.State! with { Power = false });
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                }
                await window.CheckPowerProtectionAsync();
                Assert.Equal(remainedOn ? 2 : 1, controller.Commands.Count);
                Assert.Equal(remainedOn, controller.State!.Power);
                if (!remainedOn)
                {
                    await window.ExecuteGlobalHotkeyAsync(HotkeyAction.TogglePower);
                    Assert.True(controller.State!.Power);
                    controller.PublishState(controller.State with { Power = false });
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    await window.CheckPowerProtectionAsync();
                    Assert.Equal(new[] { SoundbarCommand.PowerOff, SoundbarCommand.PowerOn, SoundbarCommand.PowerOn }, controller.Commands);
                }
            }
            finally
            {
                window.RequestExit();
                await WaitForAsync(() => controller.Disposed && !window.IsVisible);
            }
        }

        string racePath = ConfigurationTests.TestPath("manual-power-background-read.json");
        FakeSoundbarController raceController = new() { RefreshGate = new() };
        ProtectionTestClock raceClock = new();
        MainWindow raceWindow = await CreatePowerIntentWindowAsync(racePath, raceController, raceClock);
        try
        {
            Task read = raceWindow.CheckPowerProtectionAsync();
            Assert.False(read.IsCompleted);
            raceController.PowerOnNextRefresh = false;
            Task off = raceWindow.ExecuteGlobalHotkeyAsync(HotkeyAction.TogglePower);
            Assert.False(off.IsCompleted);
            Assert.Empty(raceController.Commands);
            raceController.RefreshGate.SetResult();
            await Task.WhenAll(read, off);
            raceController.RefreshGate = null;
            raceClock.Advance(TimeSpan.FromMinutes(2));
            await raceWindow.CheckPowerProtectionAsync();
            Assert.Equal(new[] { SoundbarCommand.PowerOff }, raceController.Commands);
            Assert.False(raceController.State!.Power);
        }
        finally
        {
            raceWindow.RequestExit();
            await WaitForAsync(() => raceController.Disposed && !raceWindow.IsVisible);
        }

        await ExerciseQueuedPowerCancellationAsync();
        await ExerciseRestoreWarningRecoveryAsync();
    }

    private static async Task ExerciseQueuedPowerCancellationAsync()
    {
        FakeSoundbarController controller = new() { RefreshGate = new() };
        ProtectionTestClock clock = new();
        MainWindow window = await CreatePowerIntentWindowAsync(ConfigurationTests.TestPath("queued-power-cancel.json"), controller, clock);
        try
        {
            Task read = window.CheckPowerProtectionAsync();
            controller.PowerOnNextRefresh = false;
            Task off = window.ExecuteGlobalHotkeyAsync(HotkeyAction.TogglePower);
            Assert.False(off.IsCompleted);
            ((Button)window.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await off.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(read.IsCompleted);
            Assert.Empty(controller.Commands);
            Assert.Equal(0, controller.CompletedMutations);
            Assert.DoesNotContain(Localization.Text("Operation.UncertainEffect"), ((TextBox)window.FindName("StatusMessage")).Text);
            controller.RefreshGate.SetResult();
            await read;
            controller.RefreshGate = null;
            Assert.Equal(new[] { SoundbarCommand.PowerOn }, controller.Commands);
            Assert.True(controller.State!.Power);
        }
        finally
        {
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }

    private static async Task ExerciseRestoreWarningRecoveryAsync()
    {
        FakeSoundbarController controller = new() { IgnorePowerOn = true };
        ProtectionTestClock clock = new();
        List<string> spoken = [];
        MainWindow window = await CreatePowerIntentWindowAsync(ConfigurationTests.TestPath("protection-warning-recovery.json"),
            controller, clock, message => { spoken.Add(message); return true; });
        int RestoreWarnings() => spoken.Count(message => message.StartsWith(Localization.Text("PowerProtection.Failed"), StringComparison.Ordinal));
        try
        {
            controller.PublishState(controller.State! with { Power = false });
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await window.CheckPowerProtectionAsync();
            Assert.Equal(1, RestoreWarnings());
            clock.Advance(TimeSpan.FromSeconds(60));
            controller.RefreshGate = new();
            Task read = window.CheckPowerProtectionAsync();
            ((Button)window.FindName("RefreshButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(((Button)window.FindName("CancelButton")).IsEnabled);
            controller.RefreshGate.SetResult();
            await read;
            await WaitForAsync(() => !((Button)window.FindName("CancelButton")).IsEnabled);
            controller.RefreshGate = null;
            await window.CheckPowerProtectionAsync();
            Assert.Equal(1, RestoreWarnings());
            Assert.Equal(2, controller.Commands.Count);
            clock.Advance(TimeSpan.FromSeconds(60));
            controller.IgnorePowerOn = false;
            await window.CheckPowerProtectionAsync();
            Assert.True(controller.State!.Power);
            clock.Advance(TimeSpan.FromSeconds(60));
            controller.IgnorePowerOn = true;
            controller.PublishState(controller.State with { Power = false });
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await window.CheckPowerProtectionAsync();
            Assert.Equal(2, RestoreWarnings());
        }
        finally
        {
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }

    private static async Task<MainWindow> CreatePowerIntentWindowAsync(string path, FakeSoundbarController controller,
        ProtectionTestClock clock, Func<string, bool>? speakMessage = null)
    {
        Assert.True(new UiPreferences
        {
            FirstRunCompleted = true, Language = Localization.Culture.TwoLetterISOLanguageName, Theme = "Dark", ConfirmExit = false,
            KeepPoweredOn = true, CloseToTray = true, SavedDevices = [new("Test soundbar, not hardware", 1)],
            StartupDeviceAddress = 1, AutoConnectAtStartup = true
        }.TrySave(out _, path));
        MainWindow window = new(controller, null, path, new FakeStartupRegistration(), powerProtectionClock: clock,
            speakMessage: speakMessage)
        {
            ShowActivated = false, ShowInTaskbar = false
        };
        window.Show();
        await WaitForAsync(() => controller.IsConnected && ((Button)window.FindName("RefreshButton")).IsEnabled);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        return window;
    }
}
