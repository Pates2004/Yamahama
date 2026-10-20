using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace YamahaRemote.Windows;

public partial class MainWindow
{
    private readonly Dictionary<TabItem, SectionWindow> _sectionWindows = [];
    private bool _suppressSectionFocus;
    private IInputElement? _lastMainContentFocus;
    private long _sectionFocusGeneration;
    private long _focusInteractionGeneration;

    private void InitializeSectionViews()
    {
        PreviewKeyDown += OnFocusInteraction;
        PreviewMouseDown += OnFocusInteraction;
        foreach (TabItem page in new[] { InformationTab, SettingsTab, HelpTab, FirmwareHelpTab })
        {
            BindSectionLabels((DependencyObject)page.Content);
            MainTabs.Items.Remove(page);
        }
        MainTabs.SelectedItem = ControlTab;
        PreviewGotKeyboardFocus += (_, eventArgs) =>
        {
            if (eventArgs.NewFocus is FrameworkElement element && Window.GetWindow(element) == this
                && element is not MenuItem && element is not Menu && element is not Window)
                _lastMainContentFocus = element;
        };
    }

    private void OnFocusInteraction(object sender, InputEventArgs eventArgs)
    {
        if (_busy)
            _focusInteractionGeneration++;
    }

    private void BindSectionLabels(DependencyObject element)
    {
        if (element is Label label)
        {
            if (BindingOperations.GetBinding(label, Label.TargetProperty) is { ElementName.Length: > 0 } binding
                && FindName(binding.ElementName) is UIElement target)
            {
                label.Target = target;
                AutomationProperties.SetLabeledBy(target, label);
            }
        }
        foreach (DependencyObject child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
            BindSectionLabels(child);
    }

    private Window GetDialogOwner() => _sectionWindows.Values.FirstOrDefault(window => window.IsActive) is Window active ? active : this;

    internal SectionWindow OpenSettingsWindow() => OpenSectionWindow(SettingsTab, "SettingsWindow", "Window.SettingsTitle", SettingsLanguageChoice);

    internal SectionWindow OpenHelpWindow() => OpenHelpWindow(null);

    internal SectionWindow OpenHelpWindow(UIElement? initialFocus)
    {
        bool alreadyOpen = _sectionWindows.ContainsKey(HelpTab);
        SectionWindow section = OpenSectionWindow(HelpTab, "HelpWindow", "Window.HelpTitle", initialFocus ?? HelpReadout);
        if (alreadyOpen && initialFocus is not null)
            initialFocus.Focus();
        return section;
    }

    internal SectionWindow OpenFirmwareWindow() => OpenSectionWindow(FirmwareHelpTab, "FirmwareWindow", "Window.FirmwareTitle", FirmwareDeviceReadout);

    internal SectionWindow OpenInformationWindow() => OpenSectionWindow(InformationTab, "InformationWindow", "Window.InformationTitle", InformationStatus);

    private SectionWindow OpenSectionWindow(TabItem page, string identifier, string titleKey, UIElement initialFocus)
    {
        if (_closing)
            throw new InvalidOperationException("The application is closing.");
        _sectionFocusGeneration++;
        if (_sectionWindows.TryGetValue(page, out SectionWindow? existing))
        {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return existing;
        }
        FrameworkElement content = (FrameworkElement)page.Content;
        IInputElement? returnFocus = _lastMainContentFocus;
        bool isSettings = page == SettingsTab;
        if (isSettings)
        {
            BeginSettings();
            SettingsTabs.SelectedItem = AppearanceSettingsTab;
        }
        page.Content = null;
        SectionWindow section = new(content, identifier, titleKey) { Owner = this };
        section.PreviewKeyDown += OnFocusInteraction;
        section.PreviewMouseDown += OnFocusInteraction;
        if (isSettings)
            section.ConfigureSettings(CommitSettings, CancelSettings);
        _sectionWindows.Add(page, section);
        section.ApplyPreferences(_uiPreferences);
        section.UpdateStatus(StatusMessage.Text, _statusIsImportant || _uiPreferences.ShowOperationMessages);
        bool navigationStarted = false;
        section.PreviewKeyDown += (_, _) => navigationStarted = true;
        section.PreviewMouseDown += (_, _) => navigationStarted = true;
        section.ContentRendered += (_, _) =>
        {
            if (navigationStarted || isSettings && SettingsTabs.SelectedItem != AppearanceSettingsTab)
                return;
            initialFocus.Focus();
        };
        section.Closed += (_, _) =>
        {
            section.DetachPage();
            page.Content = content;
            _sectionWindows.Remove(page);
            long focusGeneration = ++_sectionFocusGeneration;
            if (!_closing && !_suppressSectionFocus && IsVisible && WindowState != WindowState.Minimized)
            {
                _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    if (_closing || !IsVisible || WindowState == WindowState.Minimized || focusGeneration != _sectionFocusGeneration)
                        return;
                    Activate();
                    if (returnFocus is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } previous)
                        previous.Focus();
                    else
                        FocusMainContent();
                }));
            }
        };
        section.Show();
        return section;
    }

    internal bool CloseSectionWindows()
    {
        _suppressSectionFocus = true;
        try
        {
            foreach (SectionWindow section in _sectionWindows.OrderByDescending(entry => entry.Key == SettingsTab).Select(entry => entry.Value).ToArray())
            {
                section.Close();
                if (_sectionWindows.ContainsValue(section))
                    return false;
            }
            return true;
        }
        finally
        {
            _suppressSectionFocus = false;
        }
    }

    private void ApplySectionPreferences()
    {
        foreach (SectionWindow section in _sectionWindows.Values)
        {
            section.ApplyPreferences(_uiPreferences);
            section.UpdateStatus(StatusMessage.Text, _statusIsImportant || _uiPreferences.ShowOperationMessages);
        }
    }

    private void FocusMainContent()
    {
        if (MainTabs.SelectedItem == DevicesTab)
            SavedDevicesList.Focus();
        else
            ConnectionSummary.Focus();
    }

    private void OnOpenSettings(object sender, RoutedEventArgs eventArgs)
    {
        if (!_closing)
            OpenSettingsWindow();
    }

    private void OnOpenFirmware(object sender, RoutedEventArgs eventArgs)
    {
        if (!_closing)
            OpenFirmwareWindow();
    }

    private void OnOpenInformation(object sender, RoutedEventArgs eventArgs)
    {
        if (!_closing)
            OpenInformationWindow();
    }
}
