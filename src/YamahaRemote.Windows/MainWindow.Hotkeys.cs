using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using YamahaRemote.Core;

namespace YamahaRemote.Windows;

public partial class MainWindow
{
    private GlobalHotkeyManager? _globalHotkeys;
    private readonly IHotkeyBackend? _hotkeyBackend;
    private readonly Dictionary<HotkeyAction, TextBox> _hotkeyFields = [];
    private bool _capturingHotkey;
    private int _globalHotkeyDepth;

    protected override void OnSourceInitialized(EventArgs eventArgs)
    {
        base.OnSourceInitialized(eventArgs);
        _globalHotkeys = new GlobalHotkeyManager(new WindowInteropHelper(this).Handle,
            action => _ = ExecuteGlobalHotkeyAsync(action), _hotkeyBackend);
        if (!_globalHotkeys.TryApply(_uiPreferences.GlobalHotkeysEnabled, _uiPreferences.Hotkeys, out var error))
            _preferenceLoadWarning = DescribeHotkeyError(error!);
    }

    private void PopulateHotkeyControls()
    {
        if (_hotkeyFields.Count == 0)
        {
            foreach (HotkeyAction action in Enum.GetValues<HotkeyAction>())
            {
                Grid row = new() { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                TextBox field = new() { Tag = action, MaxLength = 128, MinWidth = 80, VerticalContentAlignment = VerticalAlignment.Center };
                AutomationProperties.SetAutomationId(field, "Hotkey." + action);
                field.TextChanged += (_, _) =>
                {
                    ChangeSettings(preferences => preferences with
                    {
                        Hotkeys = Enum.GetValues<HotkeyAction>().Select(key =>
                            new HotkeyBinding(key, _hotkeyFields[key].Text)).ToArray()
                    });
                };
                Label label = new() { Target = field, VerticalContentAlignment = VerticalAlignment.Center };
                label.SetResourceReference(StyleProperty, "WrappingReadoutLabelStyle");
                label.SetResourceReference(ContentControl.ContentProperty, HotkeyActionKey(action));
                AutomationProperties.SetLabeledBy(field, label);
                Button capture = new() { Tag = action, VerticalAlignment = VerticalAlignment.Center };
                capture.SetResourceReference(ContentControl.ContentProperty, "Hotkeys.Capture");
                AutomationProperties.SetAutomationId(capture, "CaptureHotkey." + action);
                capture.Click += OnCaptureHotkey;
                Grid.SetColumn(field, 1);
                Grid.SetColumn(capture, 2);
                row.Children.Add(label);
                row.Children.Add(field);
                row.Children.Add(capture);
                HotkeyRows.Children.Add(row);
                _hotkeyFields.Add(action, field);
            }
        }
        GlobalHotkeysCheckBox.IsChecked = SettingsPreferences.GlobalHotkeysEnabled;
        foreach ((HotkeyAction action, TextBox field) in _hotkeyFields)
        {
            field.Text = SettingsPreferences.Hotkeys.FirstOrDefault(binding => binding.Action == action)?.Gesture ?? string.Empty;
            AutomationProperties.SetName(field, Localization.Text("Hotkeys.Gesture", Localization.Text(HotkeyActionKey(action))));
            Button capture = (Button)((Grid)field.Parent).Children[2];
            AutomationProperties.SetName(capture, Localization.Text("Hotkeys.CaptureAction", Localization.Text(HotkeyActionKey(action))));
        }
    }

    private void OnGlobalHotkeysChanged(object sender, RoutedEventArgs eventArgs) =>
        ChangeSettings(preferences => preferences with { GlobalHotkeysEnabled = GlobalHotkeysCheckBox.IsChecked == true });

    private void OnCaptureHotkey(object sender, RoutedEventArgs eventArgs)
    {
        if (_closing || _busy || sender is not Button { Tag: HotkeyAction action })
            return;
        if (_globalHotkeys is not null && !_globalHotkeys.TryApply(false, _uiPreferences.Hotkeys, out var suspendError))
        {
            SetStatus(DescribeHotkeyError(suspendError!), true);
            return;
        }
        _capturingHotkey = true;
        try
        {
            HotkeyCaptureWindow capture = new(Localization.Text(HotkeyActionKey(action))) { Owner = GetDialogOwner() };
            TextScaling.Apply(capture, _uiPreferences.TextScale);
            if (capture.ShowDialog() == true && capture.Gesture is not null)
                _hotkeyFields[action].Text = capture.Gesture;
        }
        finally
        {
            _capturingHotkey = false;
            if (!_closing && _globalHotkeys is not null
                && !_globalHotkeys.TryApply(_uiPreferences.GlobalHotkeysEnabled, _uiPreferences.Hotkeys, out var restoreError))
                SetStatus(DescribeHotkeyError(restoreError!), true);
            if (!_closing)
                _hotkeyFields[action].Focus();
        }
    }

    private static string DescribeHotkeyError(HotkeyRegistrationError error) => error.Reason switch
    {
        HotkeyRegistrationErrorReason.DuplicateGesture => Localization.Text("Hotkeys.Duplicate", error.Gesture),
        HotkeyRegistrationErrorReason.RegistrationFailed => Localization.Text("Hotkeys.RegistrationFailed", error.Gesture, error.Win32Code),
        HotkeyRegistrationErrorReason.RestorationFailed or HotkeyRegistrationErrorReason.UnregistrationFailed => Localization.Text("Hotkeys.RestoreFailed"),
        _ => Localization.Text("Hotkeys.Invalid", Localization.Text(HotkeyActionKey(error.Action)), error.Gesture)
    };

    internal async Task ExecuteGlobalHotkeyAsync(HotkeyAction action)
    {
        _globalHotkeyDepth++;
        try { await RunGlobalHotkeyAsync(action); }
        finally { _globalHotkeyDepth--; }
    }

    private async Task RunGlobalHotkeyAsync(HotkeyAction action)
    {
        if (_closing || _capturingHotkey)
            return;
        if (action == HotkeyAction.ShowWindow)
        {
            WindowActivation.Restore(this);
            return;
        }
        if (!CanAdjust)
        {
            SetStatus(Localization.Text(!Connected ? "Hotkeys.NotConnected" : "Hotkeys.Unavailable"), important: true);
            return;
        }
        SoundbarState state = _lastState!;
        if (action != HotkeyAction.TogglePower && RejectStandbyEdit())
            return;
        if (action is HotkeyAction.VolumeUp or HotkeyAction.VolumeDown)
        {
            if (state.Volume is >= 0 and <= 50)
            {
                int target = Math.Clamp(state.Volume + (action == HotkeyAction.VolumeUp ? 1 : -1), 0, 50);
                if (target != state.Volume)
                    await SetVolumeAsync(target);
            }
            return;
        }
        SoundbarCommand? command = action switch
        {
            HotkeyAction.TogglePower => state.Power ? SoundbarCommand.PowerOff : SoundbarCommand.PowerOn,
            HotkeyAction.ToggleMute => state.Muted ? SoundbarCommand.MuteOff : SoundbarCommand.MuteOn,
            HotkeyAction.SubwooferUp when state.Subwoofer is >= -4 and < 4 => SoundbarCommand.SubwooferUp,
            HotkeyAction.SubwooferDown when state.Subwoofer is > -4 and <= 4 => SoundbarCommand.SubwooferDown,
            HotkeyAction.InputTv => SoundbarCommand.InputTv,
            HotkeyAction.InputOptical => SoundbarCommand.InputOptical,
            HotkeyAction.InputBluetooth => SoundbarCommand.InputBluetooth,
            HotkeyAction.InputAnalog => SoundbarCommand.InputAnalog,
            HotkeyAction.ModeStereo => SoundbarCommand.Stereo,
            HotkeyAction.ModeStandard => SoundbarCommand.Standard,
            HotkeyAction.ModeMovie3D => SoundbarCommand.Movie3D,
            HotkeyAction.ModeGame => SoundbarCommand.Game,
            HotkeyAction.ToggleClearVoice => state.ClearVoice ? SoundbarCommand.ClearVoiceOff : SoundbarCommand.ClearVoiceOn,
            HotkeyAction.ToggleBassExtension => state.BassExtension ? SoundbarCommand.BassExtensionOff : SoundbarCommand.BassExtensionOn,
            HotkeyAction.BrightnessBright => SoundbarCommand.IndicatorsBright,
            HotkeyAction.BrightnessDim => SoundbarCommand.IndicatorsDim,
            HotkeyAction.BrightnessOff => SoundbarCommand.IndicatorsOff,
            _ => null
        };
        if (command is not null)
            await SendCommandAsync(command.Value);
    }

    private static string HotkeyActionKey(HotkeyAction action) => action switch
    {
        HotkeyAction.VolumeUp => "Command.IncreaseVolume",
        HotkeyAction.VolumeDown => "Command.DecreaseVolume",
        HotkeyAction.SubwooferUp => "Command.IncreaseSubwoofer",
        HotkeyAction.SubwooferDown => "Command.DecreaseSubwoofer",
        HotkeyAction.InputTv => "Command.InputTv",
        HotkeyAction.InputOptical => "Command.InputOptical",
        HotkeyAction.InputBluetooth => "Command.InputBluetooth",
        HotkeyAction.InputAnalog => "Command.InputAnalog",
        HotkeyAction.ModeStereo => "Command.Stereo",
        HotkeyAction.ModeStandard => "Command.Standard",
        HotkeyAction.ModeMovie3D => "Command.Movie3D",
        HotkeyAction.ModeGame => "Command.Game",
        HotkeyAction.BrightnessBright => "Command.IndicatorsBright",
        HotkeyAction.BrightnessDim => "Command.IndicatorsDim",
        HotkeyAction.BrightnessOff => "Command.IndicatorsOff",
        _ => "Hotkeys." + action
    };
}
