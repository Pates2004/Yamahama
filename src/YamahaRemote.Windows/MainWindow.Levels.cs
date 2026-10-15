using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using YamahaRemote.Core;

namespace YamahaRemote.Windows;

public partial class MainWindow
{
    private ConfirmedSlider? _activeLevelSlider;
    private int? _pendingLevelValue;

    private void OnLevelControlModeChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (VolumeControlModeChoice.SelectedItem is ComboBoxItem { Tag: LevelControlMode volume }
            && SubwooferControlModeChoice.SelectedItem is ComboBoxItem { Tag: LevelControlMode subwoofer })
            ChangeSettings(preferences => preferences with { VolumeControlMode = volume, SubwooferControlMode = subwoofer });
    }

    private void ApplyLevelPreferences()
    {
        VolumeControlModeChoice.SelectedItem = VolumeControlModeChoice.Items.Cast<ComboBoxItem>()
            .First(item => item.Tag is LevelControlMode mode && mode == SettingsPreferences.VolumeControlMode);
        SubwooferControlModeChoice.SelectedItem = SubwooferControlModeChoice.Items.Cast<ComboBoxItem>()
            .First(item => item.Tag is LevelControlMode mode && mode == SettingsPreferences.SubwooferControlMode);
    }

    private void UpdateLevelPresentation()
    {
        UpdateLevel(VolumeSlider, VolumeButtons, ControlRow.Volume, _uiPreferences.VolumeControlMode, _lastState?.Volume);
        UpdateLevel(SubwooferSlider, SubwooferButtons, ControlRow.Subwoofer, _uiPreferences.SubwooferControlMode, _lastState?.Subwoofer);
    }

    private void UpdateLevel(ConfirmedSlider slider, UIElement buttons, ControlRow row, LevelControlMode mode, int? value)
    {
        bool hidden = _uiPreferences.HiddenRows.Contains(row);
        slider.Visibility = !hidden && mode == LevelControlMode.Slider ? Visibility.Visible : Visibility.Collapsed;
        buttons.Visibility = !hidden && mode == LevelControlMode.Buttons ? Visibility.Visible : Visibility.Collapsed;
        bool valid = value is int level && level >= slider.Minimum && level <= slider.Maximum;
        if (valid && Connected && _stateIsCurrent)
            slider.ConfirmValue(value!.Value, _activeLevelSlider == slider);
        slider.IsAdjustmentBlocked = IsSoundbarInStandby;
        AutomationProperties.SetItemStatus(slider, IsSoundbarInStandby
            ? Localization.Text("State.PowerOffEditBlocked") : ReadoutStatus.Text);
        slider.IsEnabled = !_closing && Connected && _stateIsCurrent && valid &&
            (!_busy || _activeLevelSlider == slider && _canCancel);
    }

    private async void OnLevelValueRequested(object sender, RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        ConfirmedSlider slider = (ConfirmedSlider)sender;
        int requested = (int)Math.Round(eventArgs.NewValue);
        if (_closing || !slider.IsEnabled || !double.IsFinite(eventArgs.NewValue) ||
            requested < slider.Minimum || requested > slider.Maximum ||
            _activeLevelSlider is not null && _activeLevelSlider != slider ||
            _activeLevelSlider is null && !CanAdjust)
        {
            slider.ConfirmValue(slider.ConfirmedValue, false);
            return;
        }
        if (RejectStandbyEdit())
        {
            _pendingLevelValue = null;
            slider.ConfirmValue(slider.ConfirmedValue, false);
            return;
        }
        _pendingLevelValue = requested;
        if (_activeLevelSlider is not null)
            return;
        _activeLevelSlider = slider;
        try
        {
            while (_pendingLevelValue is int target && CanAdjust)
            {
                _pendingLevelValue = null;
                if (slider == VolumeSlider)
                {
                    if (_lastState!.Volume != target)
                        await SetVolumeAsync(target);
                }
                else if (_lastState!.Subwoofer != target)
                    await SetSubwooferLevelAsync(target);
            }
        }
        finally
        {
            _pendingLevelValue = null;
            _activeLevelSlider = null;
            if (!_closing)
            {
                slider.ConfirmValue(slider.ConfirmedValue, false);
                UpdateLevelPresentation();
            }
        }
    }

    private Task SetSubwooferLevelAsync(int target) => ExecuteAsync(
        Localization.Text("Levels.SubwooferPending", target), async token =>
        {
            for (int step = 0; step < 8 && _lastState!.Subwoofer != target; step++)
            {
                token.ThrowIfCancellationRequested();
                SoundbarState before = _controller.State ?? throw new LocalizedOperationException("Readout.FreshStateRequired");
                if (before.Subwoofer is < -4 or > 4 || before.ReceivedAt <= _unconfirmedThrough)
                    throw new LocalizedOperationException("Readout.FreshStateRequired");
                if (!before.Power)
                    throw new LocalizedOperationException("State.PowerOffEditBlocked");
                if (before.Subwoofer == target)
                {
                    ApplyState(before);
                    break;
                }
                int direction = Math.Sign(target - before.Subwoofer);
                await _controller.SendAsync(direction > 0 ? SoundbarCommand.SubwooferUp : SoundbarCommand.SubwooferDown, token);
                token.ThrowIfCancellationRequested();
                CaptureFreshState(before);
                if (_lastState!.Subwoofer != before.Subwoofer + direction)
                    throw new LocalizedOperationException("Levels.Unconfirmed");
                if (_pendingLevelValue is not null)
                    break;
            }
            if (_pendingLevelValue is null && _lastState!.Subwoofer != target)
                throw new LocalizedOperationException("Levels.Unconfirmed");
            return Localization.Text("Levels.SubwooferConfirmed", _lastState!.Subwoofer);
        }, requiresConnection: true, changesState: true, invalidateOnFailure: true, requiresPower: true);

    private bool IsLevelReportedBySlider(ControlRow row) => row switch
    {
        ControlRow.Volume => _uiPreferences.VolumeControlMode == LevelControlMode.Slider && VolumeSlider.IsKeyboardFocusWithin,
        ControlRow.Subwoofer => _uiPreferences.SubwooferControlMode == LevelControlMode.Slider && SubwooferSlider.IsKeyboardFocusWithin,
        _ => false
    };
}
