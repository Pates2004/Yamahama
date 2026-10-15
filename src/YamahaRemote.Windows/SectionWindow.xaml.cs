using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;

namespace YamahaRemote.Windows;

public partial class SectionWindow : Window
{
    private Func<bool>? _commitSettings;
    private Func<bool>? _cancelSettings;
    private bool _accepted;
    private bool _checkingClose;

    internal SectionWindow(FrameworkElement page, string identifier, string titleKey)
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, identifier);
        SetResourceReference(TitleProperty, titleKey);
        SectionContent.Content = page;
        BindSectionAppearance(page, true);
        Closing += (_, eventArgs) =>
        {
            if (_checkingClose)
            {
                eventArgs.Cancel = true;
                return;
            }
            _checkingClose = true;
            try
            {
                if (!_accepted && _cancelSettings is not null && !_cancelSettings())
                    eventArgs.Cancel = true;
            }
            finally
            {
                _checkingClose = false;
            }
        };
    }

    internal void ConfigureSettings(Func<bool> commit, Func<bool> cancel)
    {
        _commitSettings = commit;
        _cancelSettings = cancel;
        SectionCloseButton.Visibility = Visibility.Collapsed;
        SettingsOkButton.Visibility = SettingsCancelButton.Visibility = Visibility.Visible;
        SettingsOkButton.IsDefault = true;
    }

    private void OnAcceptSettings(object sender, RoutedEventArgs eventArgs)
    {
        if (_commitSettings?.Invoke() != true)
            return;
        _accepted = true;
        Close();
    }

    internal void DetachPage()
    {
        if (SectionContent.Content is DependencyObject page)
            BindSectionAppearance(page, false);
        SectionContent.Content = null;
    }

    private void BindSectionAppearance(DependencyObject element, bool attach)
    {
        if (element is Label or TextBox)
            Bind(Control.ForegroundProperty, nameof(Foreground));
        if (element is TextBox)
            Bind(Control.BackgroundProperty, nameof(Background));
        foreach (DependencyObject child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
            BindSectionAppearance(child, attach);

        void Bind(DependencyProperty property, string path)
        {
            if (attach)
                BindingOperations.SetBinding(element, property, new Binding(path) { Source = this });
            else
                BindingOperations.ClearBinding(element, property);
        }
    }

    internal void ApplyPreferences(UiPreferences preferences)
    {
        Language = XmlLanguage.GetLanguage(Localization.Culture.Name);
        TextScaling.Apply(this, preferences.TextScale);
        UsageHints.SetEnabled(this, preferences.ShowHints);
    }

    internal void UpdateStatus(string message, bool visible)
    {
        SectionStatusMessage.Text = message;
        SectionStatusMessage.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(SectionStatusMessage, Localization.Text("Status.MessagePrefix") + message);
        AutomationProperties.SetLiveSetting(SectionStatusMessage, visible ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off);
    }

    internal void AnnounceStatus()
    {
        if (!SectionStatusMessage.IsVisible)
            return;
        try
        {
            AutomationPeer? peer = UIElementAutomationPeer.FromElement(SectionStatusMessage)
                ?? UIElementAutomationPeer.CreatePeerForElement(SectionStatusMessage);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ElementNotAvailableException or COMException)
        {
            Trace.WriteLine(exception);
        }
    }

    internal bool FocusStatus() => SectionStatusMessage.IsVisible && SectionStatusMessage.Focus();

    private void OnCloseSection(object sender, RoutedEventArgs eventArgs) => Close();

    private void OnSectionKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Escape || eventArgs.Handled)
            return;
        eventArgs.Handled = true;
        Close();
    }
}
