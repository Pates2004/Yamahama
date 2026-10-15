using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static async Task ExerciseAutoConnectAsync()
    {
        foreach (bool autoConnect in new[] { false, true })
        foreach (bool powerOn in new[] { false, true })
        {
            string path = ConfigurationTests.TestPath($"automatic-{autoConnect}-{powerOn}.json");
            UiPreferences settings = new()
            {
                ConfirmExit = false, FirstRunCompleted = true, Language = "en", Theme = "Light",
                SavedDevices = [new("First saved", 1), new("Selected saved", 2)], StartupDeviceAddress = 2,
                AutoConnectAtStartup = autoConnect, PowerOnAtStartup = powerOn
            };
            Assert.True(settings.TrySave(out _, path));
            FakeSoundbarController controller = new() { InitiallyPoweredOn = false };
            MainWindow window = new(controller, null, path, new FakeStartupRegistration())
            {
                ShowActivated = false, ShowInTaskbar = false, WindowState = WindowState.Minimized
            };
            window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await WaitForAsync(() => !((Button)window.FindName("CancelButton")).IsEnabled);
            Assert.Equal(autoConnect || powerOn ? new ulong[] { 2 } : [], controller.ConnectionAddresses);
            Assert.Equal(powerOn ? new[] { SoundbarCommand.PowerOn } : [], controller.Commands);
            if (autoConnect && !powerOn)
                Assert.False(controller.State!.Power);
            window.Hide();
            window.Show();
            await window.RunStartupDeviceAsync();
            Assert.Equal(autoConnect || powerOn ? 1 : 0, controller.ConnectionAddresses.Count);
            ((CheckBox)window.FindName("AutoConnectAtStartupCheckBox")).IsChecked = !autoConnect;
            Assert.True(window.CommitSettings());
            await window.RunStartupDeviceAsync();
            Assert.Equal(autoConnect || powerOn ? 1 : 0, controller.ConnectionAddresses.Count);
            Assert.Equal(!autoConnect, UiPreferences.Load(out _, path).AutoConnectAtStartup);
            window.Close();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
        string failedPath = ConfigurationTests.TestPath("automatic-failure.json");
        Assert.True(new UiPreferences
        {
            ConfirmExit = false, FirstRunCompleted = true, Language = "en", Theme = "Light", SavedDevices = [new("Saved", 1)],
            StartupDeviceAddress = 1, AutoConnectAtStartup = true
        }.TrySave(out _, failedPath));
        FakeSoundbarController failedController = new() { FailConnection = true };
        MainWindow failed = new(failedController, null, failedPath, new FakeStartupRegistration())
        {
            ShowActivated = false, ShowInTaskbar = false, WindowState = WindowState.Minimized
        };
        failed.Show();
        await failed.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await failed.RunStartupDeviceAsync();
        Assert.Single(failedController.ConnectionAddresses);
        Assert.Empty(failedController.Commands);
        Assert.False(((Button)failed.FindName("CancelButton")).IsEnabled);
        Assert.Equal(Visibility.Visible, ((TextBox)failed.FindName("StatusMessage")).Visibility);
        CheckBox autoChoice = (CheckBox)failed.FindName("AutoConnectAtStartupCheckBox");
        using (FileStream locked = new(failedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            autoChoice.IsChecked = false;
            Assert.False(failed.CommitSettings());
            Assert.False(autoChoice.IsChecked);
        }
        ((ListBox)failed.FindName("SavedDevicesList")).SelectedIndex = 0;
        ((Button)failed.FindName("RemoveSavedDeviceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(autoChoice.IsChecked);
        Assert.True(autoChoice.IsEnabled);
        Assert.False(UiPreferences.Load(out _, failedPath).AutoConnectAtStartup);
        Assert.Null(UiPreferences.Load(out _, failedPath).StartupDeviceAddress);
        Assert.True(failed.CommitSettings());
        failed.Close();
        await WaitForAsync(() => failedController.Disposed && !failed.IsVisible);
    }
}
