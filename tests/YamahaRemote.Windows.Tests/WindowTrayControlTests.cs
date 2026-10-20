using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static async Task ExerciseTrayControlsAsync()
    {
        await ExerciseConfirmedTrayClicksAsync();
        foreach (string language in new[] { "pl", "en" })
            await ExerciseTrayControlsLanguageAsync(language);
    }

    private static async Task ExerciseConfirmedTrayClicksAsync()
    {
        ConfirmedToggleMenuItem item = new();
        IToggleProvider toggle = TrayToggle(item);
        bool? observed = null;
        int clicks = 0;
        int changes = 0;
        item.Click += (_, _) => { observed = item.IsChecked; clicks++; };
        item.Checked += (_, _) => changes++;
        item.Unchecked += (_, _) => changes++;
        MethodInfo click = typeof(MenuItem).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(item.IsCheckable);
        foreach (bool? confirmed in new bool?[] { null, false, true, null, true, false })
        {
            item.ConfirmedValue = confirmed;
            ToggleState expected = confirmed is null ? ToggleState.Indeterminate : confirmed.Value ? ToggleState.On : ToggleState.Off;
            bool checkedBefore = confirmed == true;
            int changesBefore = changes;
            int clicksBefore = clicks;
            Assert.Equal(expected, toggle.ToggleState);
            Assert.Equal(checkedBefore, item.IsChecked);
            item.IsChecked = !checkedBefore;
            Assert.Equal(checkedBefore, item.IsChecked);
            Assert.Equal(changesBefore, changes);
            toggle.Toggle();
            Assert.Equal(clicksBefore + 1, clicks);
            Assert.Equal(checkedBefore, observed);
            Assert.Equal(expected, toggle.ToggleState);
            click.Invoke(item, null);
            await item.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal(clicksBefore + 2, clicks);
            Assert.Equal(checkedBefore, observed);
            Assert.Equal(checkedBefore, item.IsChecked);
            Assert.Equal(expected, toggle.ToggleState);
            Assert.Equal(changesBefore, changes);
        }
        item.IsEnabled = false;
        int disabledClicks = clicks;
        Assert.Throws<ElementNotEnabledException>(toggle.Toggle);
        Assert.Equal(disabledClicks, clicks);
    }

    private static async Task ExerciseTrayControlsLanguageAsync(string language)
    {
        string path = ConfigurationTests.TestPath("tray-controls-" + language + ".json");
        UiPreferences preferences = new()
        {
            ConfirmExit = false, FirstRunCompleted = true, Language = language, Theme = "Dark", ShowHints = false,
            AnnounceStateChanges = false, CloseToTray = true,
            SavedDevices = [new("Tray test device (not hardware)", 1)], StartupDeviceAddress = 1,
            AutoConnectAtStartup = true, VolumeControlMode = LevelControlMode.Slider,
            SubwooferControlMode = LevelControlMode.Buttons
        };
        Assert.True(preferences.TrySave(out _, path));
        Localization.SetLanguage(language);
        FakeSoundbarController controller = new();
        MainWindow window = new(controller, null, path, new FakeStartupRegistration())
        {
            ShowActivated = false, ShowInTaskbar = false
        };
        window.Show();
        await WaitForAsync(() => controller.IsConnected && window.IsLoaded);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        T Control<T>(string name) where T : FrameworkElement => Assert.IsType<T>(window.FindName(name));
        TrayController tray = Assert.IsType<TrayController>(window.Tray);
        MenuItem volume = tray.FindItem("Tray.Volume");
        MenuItem subwoofer = tray.FindItem("Tray.Subwoofer");
        MenuItem brightness = tray.FindItem("Tray.Brightness");
        object[] roots = tray.Menu.Items.Cast<object>().ToArray();
        MenuItem[] volumeItems = volume.Items.Cast<MenuItem>().ToArray();
        MenuItem[] subwooferButtons = subwoofer.Items.Cast<MenuItem>().ToArray();
        AssertTrayLevels(volume, 0, 50, 20);
        Assert.Equal(new[] { tray.CommandItem(SoundbarCommand.SubwooferUp), tray.CommandItem(SoundbarCommand.SubwooferDown) }, subwooferButtons);
        Assert.All(subwooferButtons, item => Assert.False(item.IsCheckable));
        controller.PublishState(controller.State! with { Volume = 21, Subwoofer = 1, Brightness = IndicatorBrightness.Dim });
        await SettleAsync();
        AssertTrayLevels(volume, 0, 50, 21);
        Assert.Equal(Localization.Text("Tray.GroupValue", Localization.Text("Tray.Brightness"), Localization.Text("Brightness.Dim")), brightness.Header);
        Assert.Equal(brightness.Header, AutomationProperties.GetName(brightness));
        Assert.Equal(roots, tray.Menu.Items.Cast<object>().ToArray());
        Assert.Equal(volumeItems, volume.Items.Cast<MenuItem>().ToArray());
        Assert.Equal(subwooferButtons, subwoofer.Items.Cast<MenuItem>().ToArray());

        foreach ((SoundbarCommand selected, SoundbarCommand target) in new[]
        {
            (SoundbarCommand.IndicatorsDim, SoundbarCommand.IndicatorsOff),
            (SoundbarCommand.InputTv, SoundbarCommand.InputOptical),
            (SoundbarCommand.Standard, SoundbarCommand.Game)
        })
        {
            MenuItem previous = tray.CommandItem(selected);
            MenuItem requested = tray.CommandItem(target);
            IToggleProvider previousToggle = TrayToggle(previous);
            IToggleProvider requestedToggle = TrayToggle(requested);
            Assert.Equal(ToggleState.On, previousToggle.ToggleState);
            Assert.Equal(ToggleState.Off, requestedToggle.ToggleState);
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            controller.MutationGate = gate;
            requestedToggle.Toggle();
            await WaitForAsync(() => Control<Button>("CancelButton").IsEnabled);
            Assert.Equal(target, controller.Commands[^1]);
            Assert.False(requested.IsChecked);
            Assert.True(previous.IsChecked);
            Assert.Equal(ToggleState.Off, requestedToggle.ToggleState);
            Assert.Equal(ToggleState.On, previousToggle.ToggleState);
            Assert.Throws<ElementNotEnabledException>(requestedToggle.Toggle);
            gate.SetResult();
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            controller.MutationGate = null;
            Assert.Equal(ToggleState.On, requestedToggle.ToggleState);
            Assert.Equal(ToggleState.Off, previousToggle.ToggleState);
            Assert.True(requested.IsChecked);
            Assert.False(previous.IsChecked);
        }
        Assert.Equal(Localization.Text("Tray.GroupValue", Localization.Text("Tray.Brightness"), Localization.Text("Brightness.Off")), brightness.Header);
        controller.FailNextMutation = true;
        TrayToggle(tray.CommandItem(SoundbarCommand.IndicatorsBright)).Toggle();
        await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
        foreach (string key in new[] { "Tray.Brightness", "Tray.Modes", "Tray.Inputs" })
            AssertUnknownTraySelections(tray.FindItem(key), enabled: false);
        Assert.Equal(Localization.Text("Tray.GroupValue", Localization.Text("Tray.Brightness"), Localization.Text("Readout.UnknownFeminine")), brightness.Header);
        await RefreshAsync();
        Assert.Equal(ToggleState.On, TrayToggle(tray.CommandItem(SoundbarCommand.IndicatorsOff)).ToggleState);
        SoundbarState confirmed = controller.State!;
        tray.UpdateState(true, confirmed with { Brightness = (IndicatorBrightness)255, Mode = SoundMode.Unknown, Input = (SoundbarInput)255 }, false, false, false);
        foreach (string key in new[] { "Tray.Brightness", "Tray.Modes", "Tray.Inputs" })
            AssertUnknownTraySelections(tray.FindItem(key), enabled: true);
        tray.UpdateState(false, confirmed, false, false, false);
        AssertUnknownTraySelections(brightness, enabled: false);
        AssertUnknownTraySelections(volume, enabled: false);
        tray.UpdateState(true, confirmed, false, false, false);

        Control<ComboBox>("VolumeControlModeChoice").SelectedIndex = 0;
        Control<ComboBox>("SubwooferControlModeChoice").SelectedIndex = 1;
        Assert.Equal(volumeItems, volume.Items.Cast<MenuItem>().ToArray());
        Assert.Equal(subwooferButtons, subwoofer.Items.Cast<MenuItem>().ToArray());
        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(window.CommitSettings());
            Assert.Equal(volumeItems, volume.Items.Cast<MenuItem>().ToArray());
            Assert.Equal(subwooferButtons, subwoofer.Items.Cast<MenuItem>().ToArray());
        }
        Assert.True(window.CommitSettings());
        Assert.Same(tray, window.Tray);
        Assert.Equal(roots, tray.Menu.Items.Cast<object>().ToArray());
        Assert.Equal(new[] { tray.FindItem("Command.IncreaseVolume"), tray.FindItem("Command.DecreaseVolume") }, volume.Items.Cast<MenuItem>().ToArray());
        AssertTrayLevels(subwoofer, -4, 4, 1);
        Assert.Equal(LevelControlMode.Buttons, UiPreferences.Load(out _, path).VolumeControlMode);
        Assert.Equal(LevelControlMode.Slider, UiPreferences.Load(out _, path).SubwooferControlMode);
        MenuItem[] subwooferItems = subwoofer.Items.Cast<MenuItem>().ToArray();
        foreach (int target in new[] { 4, -4, 0 })
        {
            int before = controller.State!.Subwoofer;
            int commandsBefore = controller.Commands.Count;
            TrayToggle(TrayLevel(subwoofer, target)).Toggle();
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            Assert.Equal(target, controller.State!.Subwoofer);
            Assert.Equal(commandsBefore + Math.Abs(target - before), controller.Commands.Count);
            Assert.All(controller.Commands.Skip(commandsBefore), command => Assert.Equal(
                target > before ? SoundbarCommand.SubwooferUp : SoundbarCommand.SubwooferDown, command));
            AssertTrayLevels(subwoofer, -4, 4, target);
            Assert.Equal(subwooferItems, subwoofer.Items.Cast<MenuItem>().ToArray());
        }
        Control<ComboBox>("VolumeControlModeChoice").SelectedIndex = 1;
        Assert.True(window.CommitSettings());
        Assert.Equal(volumeItems, volume.Items.Cast<MenuItem>().ToArray());
        Assert.Equal(subwooferItems, subwoofer.Items.Cast<MenuItem>().ToArray());
        TaskCompletionSource volumeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.MutationGate = volumeGate;
        int volumeRequests = controller.VolumeRequests;
        TrayToggle(TrayLevel(volume, 31)).Toggle();
        await WaitForAsync(() => Control<Button>("CancelButton").IsEnabled);
        Assert.Equal(volumeRequests + 1, controller.VolumeRequests);
        AssertTrayLevels(volume, 0, 50, 21);
        Assert.False(TrayLevel(volume, 31).IsEnabled);
        Assert.Throws<ElementNotEnabledException>(() => TrayToggle(TrayLevel(volume, 32)).Toggle());
        volumeGate.SetResult();
        await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
        controller.MutationGate = null;
        Assert.Equal(31, controller.State!.Volume);
        AssertTrayLevels(volume, 0, 50, 31);
        foreach (int target in new[] { 0, 50 })
        {
            volumeRequests = controller.VolumeRequests;
            TrayToggle(TrayLevel(volume, target)).Toggle();
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            Assert.Equal(volumeRequests + 1, controller.VolumeRequests);
            Assert.Equal(target, controller.State!.Volume);
            AssertTrayLevels(volume, 0, 50, target);
        }
        controller.DelayMutation = true;
        TrayToggle(TrayLevel(subwoofer, 4)).Toggle();
        await WaitForAsync(() => Control<Button>("CancelButton").IsEnabled);
        AssertTrayLevels(subwoofer, -4, 4, 0);
        TrayController.InvokeItem(tray.FindItem("Control.CancelName"));
        await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
        controller.DelayMutation = false;
        AssertUnknownTraySelections(volume, enabled: false);
        AssertUnknownTraySelections(subwoofer, enabled: false);
        await RefreshAsync();
        AssertTrayLevels(volume, 0, 50, 50);
        AssertTrayLevels(subwoofer, -4, 4, 0);
        confirmed = controller.State!;
        foreach ((int invalidVolume, int invalidSubwoofer) in new[] { (-1, -5), (51, 5), (255, 255) })
        {
            controller.PublishState(confirmed with { Volume = invalidVolume, Subwoofer = invalidSubwoofer });
            await SettleAsync();
            AssertUnknownTraySelections(volume, enabled: false);
            AssertUnknownTraySelections(subwoofer, enabled: false);
            Assert.Throws<ElementNotEnabledException>(() => TrayToggle(TrayLevel(volume, 20)).Toggle());
            Assert.Throws<ElementNotEnabledException>(() => TrayToggle(TrayLevel(subwoofer, 0)).Toggle());
        }
        controller.PublishState(confirmed with { Power = false });
        await SettleAsync();
        volumeRequests = controller.VolumeRequests;
        int commands = controller.Commands.Count;
        foreach (MenuItem item in new[] { TrayLevel(volume, 20), TrayLevel(volume, 50), TrayLevel(subwoofer, 4), TrayLevel(subwoofer, 0), tray.CommandItem(SoundbarCommand.IndicatorsBright) })
        {
            Assert.True(item.IsEnabled);
            Assert.Equal(Localization.Text("State.PowerOffEditBlocked"), AutomationProperties.GetItemStatus(item));
            TrayToggle(item).Toggle();
            await SettleAsync();
            Assert.Equal(Localization.Text("State.PowerOffEditBlocked"), Control<TextBox>("StatusMessage").Text);
            Assert.Equal(volumeRequests, controller.VolumeRequests);
            Assert.Equal(commands, controller.Commands.Count);
        }
        AssertTrayLevels(volume, 0, 50, 50);
        AssertTrayLevels(subwoofer, -4, 4, 0);
        controller.PublishState(confirmed with { Volume = 20 });
        await SettleAsync();
        Control<ComboBox>("VolumeControlModeChoice").SelectedIndex = 0;
        Control<ComboBox>("SubwooferControlModeChoice").SelectedIndex = 0;
        Assert.True(window.CommitSettings());
        Assert.Equal(subwooferButtons, subwoofer.Items.Cast<MenuItem>().ToArray());
        foreach ((MenuItem item, int expectedVolume, int expectedSubwoofer) in new[]
        {
            (tray.FindItem("Command.IncreaseVolume"), 21, 0),
            (tray.FindItem("Command.DecreaseVolume"), 20, 0),
            (tray.CommandItem(SoundbarCommand.SubwooferUp), 20, 1),
            (tray.CommandItem(SoundbarCommand.SubwooferDown), 20, 0)
        })
        {
            TrayController.InvokeItem(item);
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            Assert.Equal(expectedVolume, controller.State!.Volume);
            Assert.Equal(expectedSubwoofer, controller.State.Subwoofer);
        }
        foreach (bool maximum in new[] { false, true })
        {
            controller.PublishState(controller.State! with { Volume = maximum ? 50 : 0, Subwoofer = maximum ? 4 : -4 });
            await SettleAsync();
            Assert.Equal(!maximum, tray.FindItem("Command.IncreaseVolume").IsEnabled);
            Assert.Equal(maximum, tray.FindItem("Command.DecreaseVolume").IsEnabled);
            Assert.Equal(!maximum, tray.CommandItem(SoundbarCommand.SubwooferUp).IsEnabled);
            Assert.Equal(maximum, tray.CommandItem(SoundbarCommand.SubwooferDown).IsEnabled);
        }
        tray.UpdateState(true, controller.State, false, true, true);
        Assert.False(tray.FindItem("Control.CancelName").IsEnabled);
        Assert.False(tray.FindItem("Control.RefreshName").IsEnabled);
        Assert.False(tray.FindItem("Connection.DisconnectName").IsEnabled);
        Assert.False(tray.FindItem("Control.Power").IsEnabled);
        Assert.All(volumeItems.Concat(subwooferItems), item => Assert.False(item.IsEnabled));
        tray.UpdateState(true, controller.State, false, false, false);
        window.RequestExit();
        await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        Assert.False(tray.Visible);
        Assert.Throws<ElementNotEnabledException>(() => TrayToggle(volumeItems[0]).Toggle());
        Assert.Throws<ElementNotEnabledException>(() => TrayToggle(subwooferItems[0]).Toggle());

        async Task SettleAsync() => await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

        async Task RefreshAsync()
        {
            TrayController.InvokeItem(tray.FindItem("Control.RefreshName"));
            await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled && tray.FindItem("Control.Power").IsEnabled);
            await SettleAsync();
        }
    }

    private static IToggleProvider TrayToggle(MenuItem item) => Assert.IsAssignableFrom<IToggleProvider>(
        UIElementAutomationPeer.CreatePeerForElement(item)!.GetPattern(PatternInterface.Toggle));

    private static MenuItem TrayLevel(MenuItem group, int value) =>
        Assert.Single(group.Items.Cast<MenuItem>(), item => item.Tag is int level && level == value);

    private static void AssertTrayLevels(MenuItem group, int minimum, int maximum, int confirmed)
    {
        MenuItem[] items = group.Items.Cast<MenuItem>().ToArray();
        Assert.Equal(Enumerable.Range(minimum, maximum - minimum + 1), items.Select(item => Assert.IsType<int>(item.Tag)));
        foreach (MenuItem item in items)
        {
            Assert.True(item.IsCheckable);
            int level = Assert.IsType<int>(item.Tag);
            Assert.Equal(level.ToString(Localization.Culture), item.Header);
            Assert.Equal(Localization.Text("Tray.GroupValue", Localization.Text(
                AutomationProperties.GetAutomationId(group)), level.ToString(Localization.Culture)), AutomationProperties.GetName(item));
            Assert.Equal(level == confirmed, item.IsChecked);
            Assert.Equal(level == confirmed ? ToggleState.On : ToggleState.Off, TrayToggle(item).ToggleState);
        }
        Assert.Single(items, item => item.IsChecked);
    }

    private static void AssertUnknownTraySelections(MenuItem group, bool enabled)
    {
        Assert.NotEmpty(group.Items);
        foreach (MenuItem item in group.Items)
        {
            Assert.True(item.IsCheckable);
            Assert.False(item.IsChecked);
            Assert.Equal(enabled, item.IsEnabled);
            Assert.Equal(ToggleState.Indeterminate, TrayToggle(item).ToggleState);
        }
    }
}
