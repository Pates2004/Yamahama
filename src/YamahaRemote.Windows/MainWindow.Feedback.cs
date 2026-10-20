using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using YamahaRemote.Core;

namespace YamahaRemote.Windows;

public partial class MainWindow
{
    private readonly Action<string>? _announceState;
    private SoundbarState? _feedbackState;
    private bool _statusIsImportant;

    private void OnReadoutFocus(object sender, KeyboardFocusChangedEventArgs eventArgs)
    {
        if (sender is FrameworkElement element)
            element.BringIntoView();
    }

    private (ControlRow Key, Label Label, TextBox Field, TextBlock Text)[] ParameterReadouts() =>
    [
        (ControlRow.Power, PowerReadoutLabel, PowerReadout, PowerStaticReadout),
        (ControlRow.Input, InputReadoutLabel, InputReadout, InputStaticReadout),
        (ControlRow.Volume, VolumeReadoutLabel, VolumeReadout, VolumeStaticReadout),
        (ControlRow.Mute, MuteReadoutLabel, MuteReadout, MuteStaticReadout),
        (ControlRow.Subwoofer, SubwooferReadoutLabel, SubwooferReadout, SubwooferStaticReadout),
        (ControlRow.SoundMode, ModeReadoutLabel, ModeReadout, ModeStaticReadout),
        (ControlRow.ClearVoice, ClearVoiceReadoutLabel, ClearVoiceReadout, ClearVoiceStaticReadout),
        (ControlRow.BassExtension, BassReadoutLabel, BassReadout, BassStaticReadout),
        (ControlRow.IndicatorBrightness, BrightnessReadoutLabel, BrightnessReadout, BrightnessStaticReadout)
    ];

    private void UpdateReadoutPresentation()
    {
        ValueDisplayMode mode = _uiPreferences.ValueDisplayMode;
        foreach (var entry in ParameterReadouts())
        {
            entry.Text.Text = Localization.Text($"Feedback.{entry.Key}", entry.Field.Text);
            AutomationProperties.SetName(entry.Text, entry.Text.Text);
            bool hidden = _uiPreferences.HiddenRows.Contains(entry.Key);
            entry.Field.Visibility = !hidden && mode == ValueDisplayMode.ReadOnlyFields
                ? Visibility.Visible : Visibility.Collapsed;
            entry.Label.Visibility = !hidden && (mode == ValueDisplayMode.ReadOnlyFields
                || mode == ValueDisplayMode.None && entry.Key is ControlRow.Volume or ControlRow.Subwoofer)
                ? Visibility.Visible : Visibility.Collapsed;
            entry.Label.Target = mode == ValueDisplayMode.None ? null : entry.Field;
            entry.Text.Visibility = !hidden && mode == ValueDisplayMode.StaticText
                ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateReadoutColumnWidths();
        UpdateLevelPresentation();
    }

    private void UpdateReadoutColumnWidths()
    {
        double factor = _uiPreferences.TextScale / 100.0;
        ControlRowsGrid.ColumnDefinitions[0].Width = new GridLength(150 * factor);
        ControlRowsGrid.ColumnDefinitions[1].Width = new GridLength(
            _uiPreferences.ValueDisplayMode == ValueDisplayMode.None ? 0 : 170 * factor);
        ControlRowsGrid.ColumnDefinitions[2].MinWidth = 370 * factor;
    }

    private void OnValueDisplayModeChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (ValueDisplayModeChoice.SelectedItem is ComboBoxItem { Tag: ValueDisplayMode mode })
            ChangeSettings(preferences => preferences with { ValueDisplayMode = mode });
    }

    private void UpdateOperationMessageVisibility()
    {
        bool visible = _statusIsImportant || _uiPreferences.ShowOperationMessages;
        OperationMessageLabel.Visibility = StatusMessage.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(StatusMessage, visible ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off);
    }

    private void AnnounceConfirmedState(bool deviceUpdate = false)
    {
        if (!Connected || !_stateIsCurrent || _lastState is null)
        {
            _feedbackState = null;
            return;
        }
        if (_busy && _mutationInProgress)
            return;
        SoundbarState? previous = _feedbackState;
        _feedbackState = _lastState;
        bool enabled = deviceUpdate ? _uiPreferences.AnnounceExternalStateChanges : _uiPreferences.AnnounceStateChanges;
        if (!enabled || previous is null)
            return;
        ControlRow[] changed = SoundbarFeedback.ChangedRows(previous, _lastState);
        string message = string.Join(". ", ParameterReadouts().Where(entry => changed.Contains(entry.Key)
            && (deviceUpdate || _globalHotkeyDepth != 0 || !IsLevelReportedBySlider(entry.Key))).Select(entry => entry.Text.Text));
        if (message.Length == 0)
            return;
        if (_announceState is not null)
        {
            _announceState(message);
            return;
        }
        try
        {
            UIElement target = _tray?.Menu.IsOpen == true ? _tray.Menu : GetDialogOwner();
            AutomationPeer? peer = UIElementAutomationPeer.FromElement(target) ?? UIElementAutomationPeer.CreatePeerForElement(target);
            peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent,
                message, "Yamahama.ConfirmedState");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ElementNotAvailableException or COMException)
        {
            Trace.WriteLine(exception);
        }
    }

    private void FocusOperationResult()
    {
        if (GetDialogOwner() is SectionWindow section && section.FocusStatus())
            return;
        if (StatusMessage.IsVisible)
            StatusMessage.Focus();
        else if (RefreshButton.IsEnabled && RefreshButton.IsVisible)
            RefreshButton.Focus();
        else if (SearchButton.IsEnabled && SearchButton.IsVisible)
            SearchButton.Focus();
        else
            FocusMainContent();
    }
}
