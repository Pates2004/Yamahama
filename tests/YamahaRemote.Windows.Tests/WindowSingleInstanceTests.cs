using System.IO;
using System.Windows;
using System.Windows.Controls;
using Xunit;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static async Task ExerciseSingleInstanceRestorationAsync()
    {
        string setupPath = ConfigurationTests.TestPath("single-instance-setup.json");
        File.WriteAllText(setupPath, "{}");
        FirstRunWindow setup = new(new UiPreferences(), setupPath)
        { ShowActivated = false, ShowInTaskbar = false };
        try
        {
            setup.Show();
            await WaitForAsync(() => setup.IsLoaded);
            setup.WindowState = WindowState.Minimized;
            WindowActivation.Restore(setup);
            await setup.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.True(setup.IsVisible);
            Assert.Equal(WindowState.Normal, setup.WindowState);
            Assert.Null(setup.CompletedConfiguration);
            Assert.Equal("{}", File.ReadAllText(setupPath));
        }
        finally
        {
            setup.Close();
        }

        string path = ConfigurationTests.TestPath("single-instance-restoration.json");
        Assert.True(new UiPreferences
        {
            Language = "pl", Theme = "Dark", FirstRunCompleted = true,
            CloseToTray = true, ConfirmExit = false, ShowHints = false,
            AutoConnectAtStartup = false
        }.TrySave(out _, path));
        FakeSoundbarController controller = new();
        MainWindow window = new(controller, null, path, new FakeStartupRegistration())
        { ShowActivated = false };
        try
        {
            window.Show();
            await SettleReadoutWindowAsync(window);
            string before = File.ReadAllText(path);
            for (int repeat = 0; repeat < 3; repeat++)
            {
                window.WindowState = WindowState.Minimized;
                WindowActivation.Restore(window);
                await SettleReadoutWindowAsync(window);
                Assert.Equal(WindowState.Normal, window.WindowState);
                Assert.True(window.HideForCloseShortcut());
                Assert.False(window.IsVisible);
                WindowActivation.Restore(window);
                await SettleReadoutWindowAsync(window);
                Assert.True(window.IsVisible);
                Assert.True(window.ShowInTaskbar);
                Assert.Equal(WindowState.Normal, window.WindowState);
                Assert.IsNotType<MenuItem>(System.Windows.Input.Keyboard.FocusedElement);
            }
            Assert.Empty(controller.Commands);
            Assert.Equal(before, File.ReadAllText(path));
        }
        finally
        {
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }
}
