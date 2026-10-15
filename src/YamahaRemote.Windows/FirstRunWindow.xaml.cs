using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Markup;

namespace YamahaRemote.Windows;

public partial class FirstRunWindow : Window
{
    private readonly UiPreferences _initialConfiguration;
    private readonly string? _configurationPath;
    private bool _initialized;

    internal UiPreferences? CompletedConfiguration { get; private set; }

    internal FirstRunWindow(UiPreferences configuration, string? configurationPath = null)
    {
        _initialConfiguration = configuration;
        _configurationPath = configurationPath;
        InitializeComponent();
        TextScaleChoice.ItemsSource = TextScaling.Choices(configuration.TextScale);
        TextScaleChoice.SelectedValue = configuration.TextScale;
        TextScaling.Apply(this, configuration.TextScale);
        ShowHintsChoice.IsChecked = configuration.ShowHints;
        ShowOperationMessagesChoice.IsChecked = configuration.ShowOperationMessages;
        ValueDisplayModeChoice.SelectedItem = ValueDisplayModeChoice.Items.Cast<ComboBoxItem>()
            .First(item => item.Tag is ValueDisplayMode mode && mode == configuration.ValueDisplayMode);
        AnnounceStateChangesChoice.IsChecked = configuration.AnnounceStateChanges;
        UsageHints.SetEnabled(this, configuration.ShowHints);
        _initialized = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs) => LanguageChoice.Focus();

    private void OnTextScaleChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_initialized && TextScaleChoice.SelectedItem is TextScaleOption scale)
            TextScaling.Apply(this, scale.Value);
    }

    private void OnChoiceChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_initialized)
            return;
        if (LanguageChoice.SelectedItem is ComboBoxItem { Tag: string language })
        {
            Localization.SetLanguage(language);
            Language = XmlLanguage.GetLanguage(language == "en" ? "en-US" : "pl-PL");
        }
        if (ThemeChoice.SelectedItem is ComboBoxItem { Tag: string theme })
        {
#pragma warning disable WPF0001
            Application.Current.ThemeMode = theme == "Dark" ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        }
        ContinueButton.IsEnabled = LanguageChoice.SelectedItem is ComboBoxItem && ThemeChoice.SelectedItem is ComboBoxItem;
    }

    private void OnHintsChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (_initialized)
            UsageHints.SetEnabled(this, ShowHintsChoice.IsChecked == true);
    }

    private void OnContinue(object sender, RoutedEventArgs eventArgs)
    {
        if (LanguageChoice.SelectedItem is not ComboBoxItem { Tag: string language }
            || ThemeChoice.SelectedItem is not ComboBoxItem { Tag: string theme }
            || ValueDisplayModeChoice.SelectedItem is not ComboBoxItem { Tag: ValueDisplayMode mode })
            return;
        UiPreferences configuration = _initialConfiguration with
        {
            Language = language,
            Theme = theme,
            ShowHints = ShowHintsChoice.IsChecked == true,
            ShowOperationMessages = ShowOperationMessagesChoice.IsChecked == true,
            ValueDisplayMode = mode,
            AnnounceStateChanges = AnnounceStateChangesChoice.IsChecked == true,
            TextScale = ((TextScaleOption)TextScaleChoice.SelectedItem).Value,
            FirstRunCompleted = true
        };
        if (!configuration.TrySave(out _, _configurationPath))
        {
            SetupStatus.Text = Localization.Text("Setup.SaveError");
            AutomationProperties.SetName(SetupStatus, SetupStatus.Text);
            UIElementAutomationPeer.CreatePeerForElement(SetupStatus)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            SetupStatus.Focus();
            return;
        }
        CompletedConfiguration = configuration;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs eventArgs) => Close();
}
