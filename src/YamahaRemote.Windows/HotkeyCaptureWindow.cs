using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace YamahaRemote.Windows;

internal sealed class HotkeyCaptureWindow : Window
{
    private readonly TextBox _status;
    private readonly string _actionName;
    internal string? Gesture { get; private set; }

    internal HotkeyCaptureWindow(string actionName)
    {
        _actionName = actionName;
        Title = Localization.Text("Hotkeys.CaptureTitle");
        Width = 560;
        MinWidth = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(Localization.Culture.Name);
        AutomationProperties.SetAutomationId(this, "HotkeyCaptureWindow");
        StackPanel content = new() { Margin = new Thickness(20) };
        _status = new TextBox
        {
            Text = Localization.Text("Hotkeys.CapturePrompt", actionName), IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0)
        };
        AutomationProperties.SetName(_status, Localization.Text("Hotkeys.CaptureTitle"));
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        Button cancel = new() { Content = Localization.Text("Setup.Cancel"), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => DialogResult = false;
        content.Children.Add(_status);
        content.Children.Add(cancel);
        Content = content;
        Loaded += (_, _) => _status.Focus();
        PreviewKeyDown += OnCaptureKey;
    }

    private void OnCaptureKey(object sender, KeyEventArgs eventArgs)
    {
        Key key = eventArgs.Key == Key.System ? eventArgs.SystemKey : eventArgs.Key;
        if (key == Key.Escape)
        {
            eventArgs.Handled = true;
            DialogResult = false;
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin || eventArgs.IsRepeat)
            return;
        if (key == Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
            return;
        if (eventArgs.OriginalSource is Button && Keyboard.Modifiers == ModifierKeys.None
            && key is Key.Enter or Key.Space)
            return;
        eventArgs.Handled = true;
        if (!HotkeyGestureParser.TryFromKey(eventArgs, Keyboard.Modifiers, out HotkeyGesture gesture))
        {
            _status.Text = Localization.Text("Hotkeys.Invalid", _actionName, new HotkeyGesture(key, Keyboard.Modifiers).Format());
            UIElementAutomationPeer.CreatePeerForElement(_status)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            return;
        }
        Gesture = gesture.Format();
        DialogResult = true;
    }
}
