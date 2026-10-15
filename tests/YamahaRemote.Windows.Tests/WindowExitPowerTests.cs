using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static async Task ExerciseExitPowerAsync()
    {
        foreach (string language in new[] { "pl", "en" })
        {
            Localization.SetLanguage(language);
            foreach (string key in new[] { "Shutdown.PowerOffOption", "Shutdown.PowerOffHint", "ExitPower.Saved",
                "ExitPower.Failed", "ExitPower.Uncertain", "ExitPower.ConnectionChanged", "ExitPower.NotConfirmed" })
                Assert.DoesNotContain(key, Localization.Text(key, "detail"));
            await ExerciseNoTargetStartupAsync(language);
            await ExerciseExitPowerPreferencesAsync(language);
            await ExerciseExitPowerReadbackAsync(language);
            await ExerciseExitPowerFailuresAsync(language);
            await ExerciseExitPowerDisconnectedAsync(language);
            await ExerciseExitPowerTrayAsync(language);
        }
        await ExerciseExitPowerTaskDrainAsync();
        await ExerciseExitPowerTimeoutsAsync();
    }

    private static UiPreferences ExitPowerPreferences(string language) => new()
    {
        ConfirmExit = false, FirstRunCompleted = true, Language = language, Theme = "Light", PowerOffOnExit = true,
        KeepPoweredOn = true, SavedDevices = [new("Not selected", 1), new("Selected fake device", 2)],
        StartupDeviceAddress = 2
    };

    private static async Task<MainWindow> CreateExitPowerWindowAsync(string name, UiPreferences preferences,
        FakeSoundbarController controller, List<string> warnings)
    {
        string path = ConfigurationTests.TestPath(name + ".json");
        Assert.True(preferences.TrySave(out string? warning, path));
        Assert.Null(warning);
        MainWindow window = new(controller, null, path, new FakeStartupRegistration(),
            powerProtectionClock: new ProtectionTestClock(), showShutdownWarning: warnings.Add)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowState = WindowState.Minimized
        };
        window.Show();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await WaitForAsync(() => window.IsLoaded && !((Button)window.FindName("CancelButton")).IsEnabled);
        return window;
    }

    private static async Task CloseExitPowerWindowAsync(MainWindow window, FakeSoundbarController controller)
    {
        window.RequestExit();
        await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static async Task ExerciseNoTargetStartupAsync(string language)
    {
        foreach (int savedCount in new[] { 0, 1, 2 })
        {
            string name = $"exit-startup-no-target-{language}-{savedCount}";
            UiPreferences preferences = new()
            {
                ConfirmExit = false, FirstRunCompleted = true, Language = language, Theme = "Light",
                SavedDevices = new SavedSoundbar[] { new("First", 1), new("Second", 2) }.Take(savedCount).ToArray()
            };
            List<string> warnings = [];
            FakeSoundbarController controller = new();
            MainWindow window = await CreateExitPowerWindowAsync(name, preferences, controller, warnings);
            await window.RunStartupDeviceAsync();
            Assert.Empty(controller.ConnectionAddresses);
            Assert.Empty(controller.Commands);
            Assert.Equal(0, controller.RefreshRequests);
            Assert.Equal(Visibility.Collapsed, ((TextBox)window.FindName("StatusMessage")).Visibility);
            Assert.True(((CheckBox)window.FindName("AutoConnectAtStartupCheckBox")).IsChecked);
            Assert.False(((CheckBox)window.FindName("PowerOnAtStartupCheckBox")).IsChecked);
            Assert.False(((CheckBox)window.FindName("PowerOffOnExitCheckBox")).IsChecked);
            CheckBox autoConnect = (CheckBox)window.FindName("AutoConnectAtStartupCheckBox");
            Assert.True(autoConnect.IsEnabled);
            autoConnect.IsChecked = false;
            Assert.True(window.CommitSettings());
            Assert.False(UiPreferences.Load(out _, ConfigurationTests.TestPath(name + ".json")).AutoConnectAtStartup);
            autoConnect.IsChecked = true;
            CheckBox exitPower = (CheckBox)window.FindName("PowerOffOnExitCheckBox");
            Assert.True(exitPower.IsEnabled);
            exitPower.IsChecked = true;
            Assert.True(window.CommitSettings());
            UiPreferences saved = UiPreferences.Load(out string? warning, ConfigurationTests.TestPath(name + ".json"));
            Assert.Null(warning);
            Assert.True(saved.AutoConnectAtStartup);
            Assert.True(saved.PowerOffOnExit);
            Assert.Null(saved.StartupDeviceAddress);
            Assert.Equal(savedCount, saved.SavedDevices.Length);
            await CloseExitPowerWindowAsync(window, controller);
            Assert.Empty(warnings);
        }
    }

    private static async Task ExerciseExitPowerPreferencesAsync(string language)
    {
        string name = $"exit-preferences-window-{language}";
        string path = ConfigurationTests.TestPath(name + ".json");
        List<string> warnings = [];
        FakeSoundbarController controller = new();
        MainWindow window = await CreateExitPowerWindowAsync(name,
            ExitPowerPreferences(language) with { PowerOffOnExit = false }, controller, warnings);
        CheckBox choice = Assert.IsType<CheckBox>(window.FindName("PowerOffOnExitCheckBox"));
        Assert.False(choice.IsChecked);
        Assert.True(choice.IsEnabled);
        Assert.True(choice.Focusable);
        choice.IsChecked = true;
        Assert.True(window.CommitSettings());
        UiPreferences enabled = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.True(enabled.PowerOffOnExit);
        Assert.True(enabled.AutoConnectAtStartup);
        Assert.False(enabled.PowerOnAtStartup);
        Assert.True(enabled.KeepPoweredOn);
        byte[] savedBytes = File.ReadAllBytes(path);
        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            choice.IsChecked = false;
            Assert.False(window.CommitSettings());
            Assert.False(choice.IsChecked);
        }
        Assert.Equal(savedBytes, File.ReadAllBytes(path));
        Assert.True(UiPreferences.Load(out _, path).PowerOffOnExit);
        int refreshes = controller.RefreshRequests;
        await window.PowerOffForExitAsync();
        Assert.Equal(refreshes, controller.RefreshRequests);
        Assert.Empty(controller.Commands);
        choice.IsChecked = false;
        Assert.True(window.CommitSettings());
        Assert.False(UiPreferences.Load(out _, path).PowerOffOnExit);
        controller.Operations.Clear();
        await CloseExitPowerWindowAsync(window, controller);
        Assert.Equal(new[] { "Disconnect", "Dispose" }, controller.Operations);
        Assert.Empty(controller.Commands);
        Assert.Empty(warnings);
    }

    private static async Task ExerciseExitPowerFailuresAsync(string language)
    {
        foreach (string failure in new[] { "read", "stale-read", "lost-before-write", "write", "ignored", "confirmation", "stale-confirmation", "lost-after-write" })
        {
            List<string> warnings = [];
            FakeSoundbarController controller = new();
            MainWindow window = await CreateExitPowerWindowAsync($"exit-failure-{language}-{failure}",
                ExitPowerPreferences(language), controller, warnings);
            bool attemptedPowerOff = failure is not ("read" or "stale-read" or "lost-before-write");
            string reason;
            switch (failure)
            {
                case "read":
                    controller.FailNextRefresh = true;
                    reason = Localization.Text("Error.InvalidOperation");
                    break;
                case "stale-read":
                    controller.RefreshWithoutNewState = true;
                    reason = Localization.Text("Readout.FreshStateRequired");
                    break;
                case "lost-before-write":
                    controller.BeforeRefresh = () =>
                    {
                        controller.DropConnection();
                        controller.RefreshWithoutNewState = true;
                    };
                    reason = Localization.Text("ExitPower.ConnectionChanged");
                    break;
                case "write":
                    controller.FailNextMutation = true;
                    reason = Localization.Text("Error.InvalidOperation");
                    break;
                case "ignored":
                    controller.IgnorePowerOff = true;
                    reason = Localization.Text("ExitPower.NotConfirmed");
                    break;
                case "confirmation":
                    controller.AfterCommand = command => controller.FailNextRefresh = true;
                    reason = Localization.Text("Error.InvalidOperation");
                    break;
                case "stale-confirmation":
                    controller.AfterCommand = command => controller.RefreshWithoutNewState = true;
                    reason = Localization.Text("Readout.FreshStateRequired");
                    break;
                default:
                    controller.AfterCommand = command => controller.DropConnection();
                    reason = Localization.Text("ExitPower.ConnectionChanged");
                    break;
            }
            await CloseExitPowerWindowAsync(window, controller);
            Assert.Equal(attemptedPowerOff ? new[] { SoundbarCommand.PowerOff } : [], controller.Commands);
            Assert.Equal(new ulong[] { 2 }, controller.ConnectionAddresses);
            string message = Assert.Single(warnings);
            Assert.Contains(Localization.Text(attemptedPowerOff ? "ExitPower.Uncertain" : "ExitPower.Failed", reason), message);
            Assert.Equal("Dispose", controller.Operations[^1]);
            Assert.Equal("Disconnect", controller.Operations[^2]);
        }
    }

    private static async Task ExerciseExitPowerDisconnectedAsync(string language)
    {
        foreach (bool failConnection in new[] { false, true })
        {
            List<string> warnings = [];
            FakeSoundbarController controller = new() { FailConnection = failConnection };
            MainWindow window = await CreateExitPowerWindowAsync($"exit-disconnected-{language}-{failConnection}",
                ExitPowerPreferences(language), controller, warnings);
            if (!failConnection)
            {
                controller.DropConnection();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
            controller.Operations.Clear();
            await CloseExitPowerWindowAsync(window, controller);
            Assert.Equal(new[] { "Disconnect", "Dispose" }, controller.Operations);
            Assert.Equal(new ulong[] { 2 }, controller.ConnectionAddresses);
            Assert.Empty(controller.Commands);
            Assert.Empty(warnings);
        }
    }

    private static async Task ExerciseExitPowerTrayAsync(string language)
    {
        List<string> warnings = [];
        FakeSoundbarController controller = new();
        MainWindow window = await CreateExitPowerWindowAsync($"exit-tray-{language}",
            ExitPowerPreferences(language) with { CloseToTray = true }, controller, warnings);
        controller.Operations.Clear();
        Assert.True(window.HideForCloseShortcut());
        Assert.False(window.IsVisible);
        await window.PowerOffForExitAsync();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.True(controller.IsConnected);
        Assert.False(controller.Disposed);
        Assert.Empty(controller.Operations);
        Assert.Empty(controller.Commands);
        await CloseExitPowerWindowAsync(window, controller);
        Assert.Equal(new[] { "Refresh", "PowerOff", "Refresh", "Disconnect", "Dispose" }, controller.Operations);
        Assert.Equal(new[] { SoundbarCommand.PowerOff }, controller.Commands);
        Assert.Empty(warnings);
    }

    private static async Task ExerciseExitPowerTaskDrainAsync()
    {
        foreach (bool background in new[] { false, true })
        {
            List<string> warnings = [];
            FakeSoundbarController controller = new();
            MainWindow window = await CreateExitPowerWindowAsync($"exit-task-drain-{background}",
                ExitPowerPreferences("en"), controller, warnings);
            controller.MutationGate = new();
            Task pending = Task.CompletedTask;
            if (background)
            {
                controller.PublishState(controller.State! with { Power = false });
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                pending = window.CheckPowerProtectionAsync();
                Assert.False(pending.IsCompleted);
            }
            else
                ((CheckBox)window.FindName("MuteToggle")).IsChecked = true;
            await WaitForAsync(() => controller.Commands.Count == 1);
            Assert.Equal(0, controller.CompletedMutations);
            controller.PowerOnNextRefresh = true;
            controller.BeforeRefresh = () => controller.MutationGate = null;
            await CloseExitPowerWindowAsync(window, controller);
            await pending.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(new[] { background ? SoundbarCommand.PowerOn : SoundbarCommand.MuteOn, SoundbarCommand.PowerOff },
                controller.Commands);
            Assert.Equal(1, controller.CompletedMutations);
            Assert.True(controller.CommandTokens[0].IsCancellationRequested);
            Assert.False(controller.CommandTokens[1].IsCancellationRequested);
            Assert.Contains(Localization.Text("Operation.UncertainEffect"), Assert.Single(warnings));
            Assert.Equal(new ulong[] { 2 }, controller.ConnectionAddresses);
        }
    }

    private static async Task ExerciseExitPowerTimeoutsAsync()
    {
        foreach (string phase in new[] { "read", "write", "confirmation" })
        {
            List<string> warnings = [];
            FakeSoundbarController controller = new();
            MainWindow window = await CreateExitPowerWindowAsync($"exit-timeout-{phase}",
                ExitPowerPreferences("en") with { KeepPoweredOn = false }, controller, warnings);
            FieldInfo closing = typeof(MainWindow).GetField("_closing", BindingFlags.Instance | BindingFlags.NonPublic)!;
            FieldInfo lifetime = typeof(MainWindow).GetField("_lifetimeCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!;
            CancellationTokenSource lifetimeCancellation = Assert.IsType<CancellationTokenSource>(lifetime.GetValue(window));
            closing.SetValue(window, true);
            lifetimeCancellation.Cancel();
            Assert.True(lifetimeCancellation.IsCancellationRequested);
            if (phase == "read")
                controller.RefreshGate = new();
            else if (phase == "write")
                controller.MutationGate = new();
            else
                controller.AfterCommand = command => controller.RefreshGate = new();
            controller.RefreshTokens.Clear();
            try
            {
                LocalizedOperationException exception = await Assert.ThrowsAsync<LocalizedOperationException>(
                    () => window.PowerOffForExitAsync(TimeSpan.FromMilliseconds(150)).WaitAsync(TimeSpan.FromSeconds(3)));
                Assert.Equal(Localization.Text(phase == "read" ? "ExitPower.Failed" : "ExitPower.Uncertain",
                    Localization.Text("Error.Timeout")), exception.Message);
                Assert.Equal(phase == "read" ? Array.Empty<SoundbarCommand>() : [SoundbarCommand.PowerOff], controller.Commands);
                Assert.Equal(phase == "confirmation" ? 1 : 0, controller.CompletedMutations);
                Assert.Equal(new ulong[] { 2 }, controller.ConnectionAddresses);
                Assert.All(controller.RefreshTokens.Concat(controller.CommandTokens), token =>
                {
                    Assert.True(token.IsCancellationRequested);
                    Assert.NotEqual(lifetimeCancellation.Token, token);
                });
            }
            finally
            {
                controller.RefreshGate = null;
                controller.MutationGate = null;
                controller.AfterCommand = null;
                closing.SetValue(window, false);
                ((CheckBox)window.FindName("PowerOffOnExitCheckBox")).IsChecked = false;
                Assert.True(window.CommitSettings());
                await CloseExitPowerWindowAsync(window, controller);
            }
            Assert.Empty(warnings);
        }
    }

    private static async Task ExerciseExitPowerReadbackAsync(string language)
    {
        foreach (bool initiallyPoweredOn in new[] { false, true })
        foreach (bool freshPower in new[] { false, true })
        {
            List<string> warnings = [];
            FakeSoundbarController controller = new() { InitiallyPoweredOn = initiallyPoweredOn };
            MainWindow window = await CreateExitPowerWindowAsync(
                $"exit-readback-{language}-{initiallyPoweredOn}-{freshPower}", ExitPowerPreferences(language), controller, warnings);
            Assert.Equal(new ulong[] { 2 }, controller.ConnectionAddresses);
            Assert.Empty(controller.Commands);
            controller.PowerOnNextRefresh = freshPower;
            controller.Operations.Clear();
            controller.RefreshTokens.Clear();
            SoundbarState? confirmed = null;
            controller.StateChanged += state => confirmed = state;
            await CloseExitPowerWindowAsync(window, controller);
            Assert.Equal(freshPower ? new[] { "Refresh", "PowerOff", "Refresh", "Disconnect", "Dispose" }
                : new[] { "Refresh", "Disconnect", "Dispose" }, controller.Operations);
            Assert.Equal(freshPower ? new[] { SoundbarCommand.PowerOff } : [], controller.Commands);
            Assert.Equal(new ulong[] { 2 }, controller.ConnectionAddresses);
            Assert.NotNull(confirmed);
            Assert.False(confirmed.Power);
            Assert.All(controller.RefreshTokens.Concat(controller.CommandTokens), token =>
            {
                Assert.True(token.CanBeCanceled);
                Assert.False(token.IsCancellationRequested);
                Assert.Equal(controller.RefreshTokens[0], token);
            });
            Assert.Empty(warnings);
        }
    }
}
