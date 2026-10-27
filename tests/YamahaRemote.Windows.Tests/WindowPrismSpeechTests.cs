using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;
using YamahaRemote.Core;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static async Task ExercisePrismSpeechAsync()
    {
        foreach (string language in new[] { "pl", "en" })
        {
            Localization.SetLanguage(language);
            string path = ConfigurationTests.TestPath("prism-window-" + language + ".json");
            Assert.True(new UiPreferences
            {
                Language = language, Theme = "Dark", FirstRunCompleted = true, ConfirmExit = false,
                SavedDevices = [new("Speech fixture, not hardware", 1)], StartupDeviceAddress = 1,
                AutoConnectAtStartup = true, CloseToTray = true, AnnounceExternalStateChanges = true,
                ShowOperationMessages = false, ShowHints = false
            }.TrySave(out _, path));
            List<string> direct = [];
            List<string> fallback = [];
            bool accept = true;
            FakeSoundbarController controller = new();
            MainWindow window = new(controller, null, path, new FakeStartupRegistration(), (_, _) => true,
                announceState: fallback.Add, speakMessage: message => { direct.Add(message); return accept; });
            TextBox otherContent = new();
            Window other = new() { Content = otherContent, Width = 220, Height = 120, ShowInTaskbar = false };
            T Control<T>(string name) where T : FrameworkElement => Assert.IsType<T>(window.FindName(name));
            async Task PublishAsync(SoundbarState state)
            {
                controller.PublishState(state);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
            async Task RefreshAsync()
            {
                Control<Button>("RefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForAsync(() => !Control<Button>("CancelButton").IsEnabled);
            }
            try
            {
                window.Show();
                await WaitForAsync(() => controller.IsConnected && Control<Button>("RefreshButton").IsEnabled);
                Assert.Empty(direct);
                Assert.Empty(fallback);
                foreach (string state in new[] { "Active", "Inactive", "Minimized", "Tray" })
                {
                    if (state == "Active")
                    {
                        window.Activate();
                        Control<TextBlock>("ConnectionSummary").Focus();
                        Assert.True(window.IsActive);
                    }
                    else
                    {
                        if (!other.IsVisible)
                            other.Show();
                        if (state == "Minimized")
                            window.WindowState = WindowState.Minimized;
                        if (state == "Tray")
                            Assert.True(window.HideForCloseShortcut());
                        other.Activate();
                        Assert.True(otherContent.Focus());
                        Assert.False(window.IsActive);
                    }
                    IInputElement? focus = Keyboard.FocusedElement;
                    int volume = controller.State!.Volume + 1;
                    int count = direct.Count;
                    await PublishAsync(controller.State with { Volume = volume });
                    Assert.Equal(count + 1, direct.Count);
                    Assert.Equal(Localization.Text("Feedback.Volume", volume.ToString(Localization.Culture)), direct[^1]);
                    Assert.Empty(fallback);
                    Assert.Same(focus, Keyboard.FocusedElement);
                    Assert.Equal(state != "Tray", window.IsVisible);
                    if (state != "Active")
                        Assert.True(other.IsActive);
                    await PublishAsync(controller.State!);
                    Assert.Equal(count + 1, direct.Count);
                }
                window.RestoreFromTray();
                window.WindowState = WindowState.Normal;
                Control<CheckBox>("PrismStateChangesCheckBox").IsChecked = false;
                int beforeDraft = direct.Count;
                await PublishAsync(controller.State! with { Volume = 25 });
                Assert.Equal(beforeDraft + 1, direct.Count);
                Assert.True(window.CancelSettings());
                Assert.True(Control<CheckBox>("PrismStateChangesCheckBox").IsChecked);
                Control<CheckBox>("PrismStateChangesCheckBox").IsChecked = false;
                Assert.True(window.CommitSettings());
                int beforeFallback = direct.Count;
                await PublishAsync(controller.State! with { Volume = 26 });
                Assert.Equal(beforeFallback, direct.Count);
                Assert.Equal(Localization.Text("Feedback.Volume", "26"), Assert.Single(fallback));
                Control<CheckBox>("PrismStateChangesCheckBox").IsChecked = true;
                Assert.True(window.CommitSettings());
                accept = false;
                await PublishAsync(controller.State! with { Volume = 27 });
                Assert.Equal(Localization.Text("Feedback.Volume", "27"), direct[^1]);
                Assert.Equal(direct[^1], fallback[^1]);
                Assert.Equal(2, fallback.Count);
                accept = true;
                Control<CheckBox>("AnnounceExternalStateChangesCheckBox").IsChecked = false;
                Control<CheckBox>("AnnounceStateChangesCheckBox").IsChecked = false;
                Assert.True(window.CommitSettings());
                int muted = direct.Count;
                await PublishAsync(controller.State! with { Volume = 28 });
                await window.ExecuteGlobalHotkeyAsync(HotkeyAction.VolumeUp);
                await RefreshAsync();
                Assert.Equal(muted, direct.Count);
                Assert.Equal(2, fallback.Count);

                Control<CheckBox>("ShowOperationMessagesCheckBox").IsChecked = true;
                Assert.True(window.CommitSettings());
                SectionWindow settings = window.OpenSettingsWindow();
                await SettleReadoutWindowAsync(window);
                other.Activate();
                otherContent.Focus();
                direct.Clear();
                await RefreshAsync();
                Assert.Equal(2, direct.Count);
                Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(Control<TextBox>("StatusMessage")));
                Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(ReadoutControl<TextBox>(settings, "SectionStatusMessage")));
                Assert.True(other.IsActive);
                Assert.Same(otherContent, Keyboard.FocusedElement);
                Control<CheckBox>("PrismOperationsCheckBox").IsChecked = false;
                Assert.True(window.CommitSettings());
                direct.Clear();
                await RefreshAsync();
                Assert.Empty(direct);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Control<TextBox>("StatusMessage")));
                Control<CheckBox>("ShowOperationMessagesCheckBox").IsChecked = false;
                Assert.True(window.CommitSettings());
                await PublishAsync(controller.State! with { Power = false });
                other.Activate();
                otherContent.Focus();
                direct.Clear();
                await window.ExecuteGlobalHotkeyAsync(HotkeyAction.VolumeUp);
                Assert.Equal(Localization.Text("State.PowerOffEditBlocked"), Assert.Single(direct));
                Assert.Equal(Visibility.Visible, Control<TextBox>("StatusMessage").Visibility);
                Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(Control<TextBox>("StatusMessage")));
                Assert.Same(otherContent, Keyboard.FocusedElement);
                Control<CheckBox>("PrismImportantCheckBox").IsChecked = false;
                Assert.True(window.CommitSettings());
                direct.Clear();
                await window.ExecuteGlobalHotkeyAsync(HotkeyAction.VolumeUp);
                Assert.Empty(direct);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Control<TextBox>("StatusMessage")));
                UiPreferences saved = UiPreferences.Load(out _, path);
                Assert.True(saved.UsePrismForStateChanges);
                Assert.False(saved.UsePrismForOperationMessages);
                Assert.False(saved.UsePrismForImportantMessages);
                settings.Close();
                direct.Clear();
                SoundbarState last = controller.State!;
                window.RequestExit();
                await WaitForAsync(() => controller.Disposed && !window.IsVisible);
                controller.PublishState(last with { Volume = 30 });
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Empty(direct);
            }
            finally
            {
                other.Close();
                if (!controller.Disposed)
                {
                    window.RequestExit();
                    await WaitForAsync(() => controller.Disposed && !window.IsVisible);
                }
            }
        }
    }
}
