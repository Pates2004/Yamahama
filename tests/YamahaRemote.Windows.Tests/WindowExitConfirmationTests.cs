using System.IO;
using System.Windows;
using System.Windows.Controls;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static async Task ExerciseExitConfirmationAsync()
    {
        foreach (string language in new[] { "pl", "en" })
        foreach (bool showHints in new[] { false, true })
        foreach (bool closeToTray in new[] { false, true })
        {
            string path = ConfigurationTests.TestPath($"exit-confirm-window-{language}-{showHints}-{closeToTray}.json");
            Assert.True((TransactionPreferences(language) with
            {
                ConfirmExit = true, CloseToTray = closeToTray, PowerOffOnExit = true, KeepPoweredOn = true,
                ShowHints = showHints
            }).TrySave(out _, path));
            Localization.SetLanguage(language);
            FakeSoundbarController controller = new();
            bool approve = false;
            int confirmations = 0;
            List<string> warnings = [];
            MainWindow window = new(controller, null, path, new FakeStartupRegistration(), confirm: (message, title) =>
            {
                Assert.Equal(ExpectedExitConfirmation(showHints), message);
                Assert.Equal(Localization.Text("Exit.ConfirmTitle"), title);
                Assert.Empty(controller.Commands);
                Assert.True(controller.IsConnected);
                Assert.False(controller.Disposed);
                Assert.DoesNotContain("Disconnect", controller.Operations);
                Assert.DoesNotContain("Refresh", controller.Operations);
                controller.Operations.Add("Confirm");
                confirmations++;
                return approve;
            }, showShutdownWarning: warnings.Add) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                await ShowSettingsOwnerAsync(window, controller);
                controller.Operations.Clear();
                window.Close();
                await SettleReadoutWindowAsync(window);
                Assert.Equal(closeToTray ? 0 : 1, confirmations);
                Assert.Equal(!closeToTray, window.IsVisible);
                Assert.True(controller.IsConnected);
                Assert.False(controller.Disposed);
                Assert.True(controller.State!.Power);
                Assert.Empty(controller.Commands);
                Assert.Equal(closeToTray ? Array.Empty<string>() : ["Confirm"], controller.Operations);
                if (closeToTray)
                {
                    Assert.True(Assert.IsType<TrayController>(window.Tray).Visible);
                    window.RestoreFromTray();
                    window.RequestExit();
                    await SettleReadoutWindowAsync(window);
                    Assert.Equal(1, confirmations);
                    Assert.True(window.IsVisible);
                    Assert.False(controller.Disposed);
                    window.Close();
                    Assert.False(window.IsVisible);
                    Assert.Equal(1, confirmations);
                }
                approve = true;
                window.RequestExit();
                await WaitForAsync(() => controller.Disposed && !window.IsVisible);
                Assert.Equal(2, confirmations);
                Assert.Equal(new[] { "Confirm", "Confirm", "Refresh", "PowerOff", "Refresh", "Disconnect", "Dispose" }, controller.Operations);
                Assert.Equal(new[] { SoundbarCommand.PowerOff }, controller.Commands);
                Assert.Empty(warnings);
                Assert.Null(window.Tray);
                Assert.False(window.IsVisible);
            }
            finally
            {
                approve = true;
                if (!controller.Disposed)
                {
                    window.RequestExit();
                    await WaitForAsync(() => controller.Disposed && !window.IsVisible);
                }
            }
        }
        foreach (string language in new[] { "pl", "en" })
        foreach (bool showHints in new[] { false, true })
            await ExerciseDraftedExitHintsAsync(language, showHints);
    }

    private static string ExpectedExitConfirmation(bool showHints)
    {
        string message = Localization.Text("Exit.ConfirmMessage");
        string hint = Localization.Text("Exit.ConfirmHint");
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.False(string.IsNullOrWhiteSpace(hint));
        Assert.NotEqual("Exit.ConfirmMessage", message);
        Assert.NotEqual("Exit.ConfirmHint", hint);
        Assert.EndsWith("?", message);
        Assert.DoesNotContain("\n", message);
        Assert.DoesNotContain("\r", message);
        Assert.DoesNotContain(hint, message);
        return showHints ? message + "\n\n" + hint : message;
    }

    private static async Task ExerciseDraftedExitHintsAsync(string language, bool showHints)
    {
        string path = ConfigurationTests.TestPath($"exit-confirm-draft-hints-{language}-{showHints}.json");
        Assert.True((TransactionPreferences(language) with { ConfirmExit = true, ShowHints = showHints }).TrySave(out _, path));
        Localization.SetLanguage(language);
        FakeSoundbarController controller = new();
        bool approveExit = false;
        bool approveDiscard = false;
        List<string> confirmations = [];
        MainWindow window = new(controller, null, path, new FakeStartupRegistration(), confirm: (message, title) =>
        {
            Assert.Equal(language, Localization.CurrentLanguage);
            Assert.Equal(showHints, LoadReadoutPreferences(path).ShowHints);
            Assert.True(controller.IsConnected);
            Assert.False(controller.Disposed);
            Assert.Empty(controller.Commands);
            Assert.DoesNotContain("Disconnect", controller.Operations);
            if (title == Localization.Text("Exit.ConfirmTitle"))
            {
                Assert.Equal(ExpectedExitConfirmation(showHints), message);
                confirmations.Add("Exit");
                return approveExit;
            }
            Assert.Equal(Localization.Text("Settings.DiscardTitle"), title);
            Assert.Equal(Localization.Text("Settings.DiscardConfirm"), message);
            confirmations.Add("Discard");
            return approveDiscard;
        }) { ShowActivated = false, ShowInTaskbar = false };
        try
        {
            await ShowSettingsOwnerAsync(window, controller);
            controller.Operations.Clear();
            byte[] before = File.ReadAllBytes(path);
            SectionWindow settings = window.OpenSettingsWindow();
            await SettleReadoutWindowAsync(window);
            SelectSettingsTab(window, "ControlsSettingsTab");
            CheckBox hints = ReadoutControl<CheckBox>(window, "ShowHintsCheckBox");
            hints.IsChecked = !showHints;
            ComboBox languages = ReadoutControl<ComboBox>(window, "SettingsLanguageChoice");
            languages.SelectedItem = languages.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, language == "pl" ? "en" : "pl"));
            Assert.Equal(showHints, UsageHints.GetEnabled(window));
            Assert.Equal(before, File.ReadAllBytes(path));
            window.RequestExit();
            await SettleReadoutWindowAsync(window);
            Assert.Equal(new[] { "Exit" }, confirmations);
            Assert.True(window.IsVisible && settings.IsVisible);
            Assert.Equal(!showHints, hints.IsChecked);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(controller.Operations);

            approveExit = true;
            window.RequestExit();
            await SettleReadoutWindowAsync(window);
            Assert.Equal(new[] { "Exit", "Exit", "Discard" }, confirmations);
            Assert.True(window.IsVisible && settings.IsVisible);
            Assert.Equal(!showHints, hints.IsChecked);
            Assert.Equal(showHints, UsageHints.GetEnabled(window));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(controller.Operations);

            approveDiscard = true;
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
            Assert.Equal(new[] { "Exit", "Exit", "Discard", "Exit", "Discard" }, confirmations);
            Assert.False(settings.IsVisible);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(new[] { "Disconnect", "Dispose" }, controller.Operations);
            Assert.Empty(controller.Commands);
        }
        finally
        {
            approveExit = true;
            approveDiscard = true;
            if (!controller.Disposed)
            {
                window.RequestExit();
                await WaitForAsync(() => controller.Disposed && !window.IsVisible);
            }
        }
    }
}
