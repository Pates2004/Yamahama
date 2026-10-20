using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YamahaRemote.Core;

namespace YamahaRemote.Windows;

public partial class MainWindow
{
    private TrayController? _tray;
    private WindowState _stateBeforeHide;
    private bool _taskbarBeforeHide;
    private IInputElement? _focusBeforeHide;

    internal TrayController? Tray => _tray;

    internal bool HideForCloseShortcut()
    {
        if (!_uiPreferences.CloseToTray || _closing)
            return false;
        try
        {
            EnsureTray();
            if (_tray?.Visible != true)
                return false;
            _stateBeforeHide = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
            _taskbarBeforeHide = ShowInTaskbar;
            _focusBeforeHide = Keyboard.FocusedElement;
            if (!CloseSectionWindows())
                return true;
            ShowInTaskbar = false;
            Hide();
            if (_uiPreferences.ShowHints && _uiPreferences.ShowOperationMessages)
                _tray.Notify(Localization.Text("Tray.Hidden"), false);
            return true;
        }
        catch (Exception exception)
        {
            RestoreFromTray();
            SetStatus(Localization.Text("Tray.Failed") + DescribeError(exception, false), true);
            return true;
        }
    }

    internal void RestoreFromTray()
    {
        if (_closing)
            return;
        if (!IsVisible)
        {
            ShowInTaskbar = _taskbarBeforeHide;
            Show();
            WindowState = _stateBeforeHide;
        }
        else if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        if (_focusBeforeHide is UIElement { IsVisible: true, IsEnabled: true } previous)
            previous.Focus();
        else
            FocusMainContent();
    }

    private void EnsureTray()
    {
        if (!_uiPreferences.CloseToTray)
        {
            _tray?.Dispose();
            _tray = null;
            return;
        }
        _tray ??= new TrayController(RestoreFromTray, () => { RestoreFromTray(); RequestExit(); },
            command => CanAdjust ? SendCommandAsync(command) : Task.CompletedTask,
            delta => CanAdjust && _lastState is { Volume: >= 0 and <= 50 } ? SetVolumeAsync(Math.Clamp(_lastState.Volume + delta, 0, 50)) : Task.CompletedTask,
            () => OnRefresh(this, new RoutedEventArgs()), () => OnDisconnect(this, new RoutedEventArgs()),
            () => OnCancelOperation(this, new RoutedEventArgs()),
            target => CanAdjust && target is >= 0 and <= 50 && _lastState is { Volume: >= 0 and <= 50 }
                ? SetVolumeAsync(target) : Task.CompletedTask,
            target => CanAdjust && target is >= -4 and <= 4 && _lastState is { Subwoofer: >= -4 and <= 4 }
                ? SetSubwooferLevelAsync(target) : Task.CompletedTask);
        _tray.ApplyLevelPreferences(_uiPreferences.VolumeControlMode, _uiPreferences.SubwooferControlMode);
        _tray.RefreshLabels(_uiPreferences.TextScale);
        UpdateTrayState();
    }

    private void UpdateTrayState() => _tray?.UpdateState(Connected, _stateIsCurrent ? _lastState : null, _busy, _canCancel, _closing);

    private void OnCloseToTrayChanged(object sender, RoutedEventArgs eventArgs)
    {
        ChangeSettings(preferences => preferences with { CloseToTray = CloseToTrayCheckBox.IsChecked == true });
    }

    private void ApplyExtendedPreferences()
    {
        bool wasApplying = _applyingPreferences;
        _applyingPreferences = true;
        try
        {
            CloseToTrayCheckBox.IsChecked = SettingsPreferences.CloseToTray;
            ConfirmExitCheckBox.IsChecked = SettingsPreferences.ConfirmExit;
            SettingsTextScaleChoice.ItemsSource = TextScaling.Choices(SettingsPreferences.TextScale);
            SettingsTextScaleChoice.SelectedValue = SettingsPreferences.TextScale;
            ApplyTextScale();
        }
        finally
        {
            _applyingPreferences = wasApplying;
        }
    }

    private void ApplyTextScale()
    {
        TextScaling.Apply(this, _uiPreferences.TextScale);
        ApplySectionPreferences();
        UpdateReadoutColumnWidths();
        _tray?.RefreshLabels(_uiPreferences.TextScale);
    }

    private void OnSettingsTextScaleChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (SettingsTextScaleChoice.SelectedItem is TextScaleOption selected)
            ChangeSettings(preferences => preferences with { TextScale = selected.Value });
    }
}
