using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using YamahaRemote.Core;

namespace YamahaRemote.Windows;

internal sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _ownedIcon;
    private readonly Dictionary<MenuItem, string> _labels = [];
    private readonly Dictionary<SoundbarCommand, MenuItem> _commands = [];
    private readonly List<(ConfirmedToggleMenuItem Item, Func<SoundbarState, bool> Read)> _toggles = [];
    private readonly MenuItem _status;
    private readonly MenuItem _inputs;
    private readonly MenuItem _brightness;
    private readonly MenuItem _volume;
    private readonly MenuItem _subwoofer;
    private readonly MenuItem _volumeUp;
    private readonly MenuItem _volumeDown;
    private readonly MenuItem _refresh;
    private readonly MenuItem _disconnect;
    private readonly MenuItem _cancel;
    private readonly Dictionary<int, ConfirmedToggleMenuItem> _volumeLevels;
    private readonly Dictionary<int, ConfirmedToggleMenuItem> _subwooferLevels;
    private LevelControlMode _volumeMode = LevelControlMode.Buttons;
    private LevelControlMode _subwooferMode = LevelControlMode.Buttons;
    private SoundbarState? _state;
    private bool _connected;
    private bool _busy;
    private bool _canAdjust;
    private bool _disposed;

    internal ContextMenu Menu { get; } = new() { Placement = PlacementMode.MousePoint };
    internal bool Visible => !_disposed && _icon.Visible;

    internal TrayController(Action restore, Action exit, Func<SoundbarCommand, Task> send,
        Func<int, Task> changeVolume, Action refresh, Action disconnect, Action cancel,
        Func<int, Task> setVolume, Func<int, Task> setSubwoofer)
    {
        _ownedIcon = (Icon)SystemIcons.Application.Clone();
        _icon = new Forms.NotifyIcon { Icon = _ownedIcon, Text = "Yamahama" };
        _icon.MouseDoubleClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) restore(); };
        _icon.MouseUp += (_, args) => { if (args.Button == Forms.MouseButtons.Right) ShowMenu(); };
        AddItem(Menu.Items, "Tray.Open", (_, _) => restore());
        _status = AddItem(Menu.Items, "Tray.NotConnected");
        _status.IsEnabled = false;
        Menu.Items.Add(new Separator());
        AddToggle("Control.Power", SoundbarCommand.PowerOn, SoundbarCommand.PowerOff, state => state.Power);
        AddToggle("Control.Mute", SoundbarCommand.MuteOn, SoundbarCommand.MuteOff, state => state.Muted);
        AddToggle("Control.ClearVoice", SoundbarCommand.ClearVoiceOn, SoundbarCommand.ClearVoiceOff, state => state.ClearVoice);
        AddToggle("Control.BassExtension", SoundbarCommand.BassExtensionOn, SoundbarCommand.BassExtensionOff, state => state.BassExtension);
        AddGroup("Tray.Modes", [SoundbarCommand.Stereo, SoundbarCommand.Standard, SoundbarCommand.Movie3D, SoundbarCommand.Game]);
        _inputs = AddGroup("Tray.Inputs", [SoundbarCommand.InputTv, SoundbarCommand.InputOptical, SoundbarCommand.InputBluetooth, SoundbarCommand.InputAnalog]);
        _brightness = AddGroup("Tray.Brightness", [SoundbarCommand.IndicatorsBright, SoundbarCommand.IndicatorsDim, SoundbarCommand.IndicatorsOff]);
        _volume = AddItem(Menu.Items, "Tray.Volume");
        _volumeUp = AddItem(_volume.Items, "Command.IncreaseVolume", async (sender, _) =>
        {
            if (!_disposed && _canAdjust && ((MenuItem)sender).IsEnabled)
                await changeVolume(1);
        });
        _volumeDown = AddItem(_volume.Items, "Command.DecreaseVolume", async (sender, _) =>
        {
            if (!_disposed && _canAdjust && ((MenuItem)sender).IsEnabled)
                await changeVolume(-1);
        });
        _subwoofer = AddGroup("Tray.Subwoofer", [SoundbarCommand.SubwooferUp, SoundbarCommand.SubwooferDown]);
        _volumeLevels = CreateLevelItems("Tray.Volume", 0, 50, setVolume);
        _subwooferLevels = CreateLevelItems("Tray.Subwoofer", -4, 4, setSubwoofer);
        Menu.Items.Add(new Separator());
        _refresh = AddItem(Menu.Items, "Control.RefreshName", (_, _) => refresh());
        _disconnect = AddItem(Menu.Items, "Connection.DisconnectName", (_, _) => disconnect());
        _cancel = AddItem(Menu.Items, "Control.CancelName", (_, _) => cancel());
        Menu.Items.Add(new Separator());
        AddItem(Menu.Items, "Tray.Exit", (_, _) => exit());
        RefreshLabels(100);
        UpdateState(false, null, false, false, false);
        _icon.Visible = true;

        MenuItem AddGroup(string label, SoundbarCommand[] commands)
        {
            MenuItem group = AddItem(Menu.Items, label);
            foreach (SoundbarCommand command in commands)
            {
                MenuItem item = AddItem(group.Items, CommandKey(command), async (sender, _) =>
                {
                    if (!_disposed && _canAdjust && ((MenuItem)sender).IsEnabled)
                        await send(command);
                }, command is SoundbarCommand.SubwooferUp or SoundbarCommand.SubwooferDown
                    ? new MenuItem() : new ConfirmedToggleMenuItem());
                _commands.Add(command, item);
            }
            return group;
        }

        void AddToggle(string label, SoundbarCommand enabled, SoundbarCommand disabled, Func<SoundbarState, bool> read)
        {
            ConfirmedToggleMenuItem item = new();
            AddItem(Menu.Items, label, async (_, _) =>
            {
                SoundbarState? confirmedState = _state;
                bool confirmedValue = confirmedState is not null && read(confirmedState);
                if (!_disposed && _canAdjust && item.IsEnabled && confirmedState is not null)
                    await send(confirmedValue ? disabled : enabled);
            }, item);
            _toggles.Add((item, read));
        }

        Dictionary<int, ConfirmedToggleMenuItem> CreateLevelItems(string key, int minimum, int maximum, Func<int, Task> setLevel)
        {
            Dictionary<int, ConfirmedToggleMenuItem> items = [];
            foreach (int target in Enumerable.Range(minimum, maximum - minimum + 1))
            {
                ConfirmedToggleMenuItem item = new() { Tag = target };
                AutomationProperties.SetAutomationId(item, key + ".Level." + target.ToString(CultureInfo.InvariantCulture));
                item.Click += async (_, _) =>
                {
                    if (!_disposed && _canAdjust && item.IsEnabled)
                        await setLevel(target);
                };
                items.Add(target, item);
            }
            return items;
        }
    }

    internal void ApplyLevelPreferences(LevelControlMode volumeMode, LevelControlMode subwooferMode)
    {
        if (_disposed)
            return;
        if (!Enum.IsDefined(volumeMode))
            throw new ArgumentOutOfRangeException(nameof(volumeMode));
        if (!Enum.IsDefined(subwooferMode))
            throw new ArgumentOutOfRangeException(nameof(subwooferMode));
        if (_volumeMode == volumeMode && _subwooferMode == subwooferMode)
            return;
        Menu.IsOpen = false;
        if (_volumeMode != volumeMode)
        {
            _volume.Items.Clear();
            foreach (MenuItem item in volumeMode == LevelControlMode.Slider
                ? _volumeLevels.Values.Cast<MenuItem>() : new[] { _volumeUp, _volumeDown })
                _volume.Items.Add(item);
            _volumeMode = volumeMode;
        }
        if (_subwooferMode != subwooferMode)
        {
            _subwoofer.Items.Clear();
            foreach (MenuItem item in subwooferMode == LevelControlMode.Slider
                ? _subwooferLevels.Values.Cast<MenuItem>()
                : new[] { _commands[SoundbarCommand.SubwooferUp], _commands[SoundbarCommand.SubwooferDown] })
                _subwoofer.Items.Add(item);
            _subwooferMode = subwooferMode;
        }
    }

    private void ShowMenu()
    {
        if (_disposed)
            return;
        Menu.IsOpen = true;
        Menu.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!_disposed && Menu.IsOpen)
            {
                if (PresentationSource.FromVisual(Menu) is HwndSource source)
                    SetForegroundWindow(source.Handle);
                Menu.Focus();
                ((MenuItem)Menu.Items[0]).Focus();
            }
        });
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    private MenuItem AddItem(ItemCollection items, string key, RoutedEventHandler? click = null, MenuItem? item = null)
    {
        item ??= new MenuItem();
        AutomationProperties.SetAutomationId(item, key);
        _labels.Add(item, key);
        if (click is not null)
            item.Click += click;
        items.Add(item);
        return item;
    }

    internal MenuItem FindItem(string key) => _labels.Single(pair => pair.Value == key).Key;

    internal static void InvokeItem(MenuItem item)
    {
        if (item.IsEnabled)
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    internal void RefreshLabels(int textScale)
    {
        if (_disposed)
            return;
        foreach ((MenuItem item, string key) in _labels)
        {
            string label = Localization.Text(key).Replace("_", string.Empty, StringComparison.Ordinal);
            SetLabel(item, label);
        }
        RefreshLevelLabels(_volumeLevels, "Tray.Volume");
        RefreshLevelLabels(_subwooferLevels, "Tray.Subwoofer");
        Menu.FontSize = System.Windows.SystemFonts.MessageFontSize * textScale / 100.0;
        RefreshPresentation();
    }

    private static void RefreshLevelLabels(Dictionary<int, ConfirmedToggleMenuItem> levels, string key)
    {
        foreach ((int level, ConfirmedToggleMenuItem item) in levels)
        {
            string value = level.ToString(Localization.Culture);
            item.Header = value;
            AutomationProperties.SetName(item, Localization.Text("Tray.GroupValue",
                Localization.Text(key).Replace("_", string.Empty, StringComparison.Ordinal), value));
        }
    }

    internal void UpdateState(bool connected, SoundbarState? state, bool busy, bool canCancel, bool closing)
    {
        if (_disposed)
            return;
        state = connected ? state : null;
        _state = state;
        _connected = connected;
        _busy = busy;
        _canAdjust = connected && state is not null && !busy && !closing;
        bool standby = state is { Power: false };
        string standbyStatus = Localization.Text("State.PowerOffEditBlocked");
        string unknownStatus = Localization.Text(connected ? "Tray.StateUnknown" : "Tray.NotConnected");
        foreach ((ConfirmedToggleMenuItem item, Func<SoundbarState, bool> read) in _toggles)
        {
            item.ConfirmedValue = state is null ? null : read(state);
            item.IsEnabled = _canAdjust;
            AutomationProperties.SetItemStatus(item, state is null ? unknownStatus
                : standby && _labels[item] != "Control.Power" ? standbyStatus
                : Localization.Text(read(state) ? "Tray.EnabledState" : "Tray.DisabledState"));
        }
        foreach ((SoundbarCommand command, MenuItem item) in _commands)
        {
            item.IsEnabled = _canAdjust && (standby || command switch
            {
                SoundbarCommand.SubwooferUp => state!.Subwoofer is >= -4 and < 4,
                SoundbarCommand.SubwooferDown => state!.Subwoofer is > -4 and <= 4,
                _ => true
            });
            if (item is ConfirmedToggleMenuItem selection)
                selection.ConfirmedValue = state is null || !IsSelectionKnown(command, state) ? null : Matches(command, state);
            AutomationProperties.SetItemStatus(item, state is null ? unknownStatus : standby ? standbyStatus
                : item is ConfirmedToggleMenuItem { ConfirmedValue: null } ? Localization.Text("Readout.Unknown")
                : item.IsChecked ? Localization.Text("Tray.SelectedState") : string.Empty);
        }
        _volumeUp.IsEnabled = _canAdjust && (standby || state!.Volume is >= 0 and < 50);
        _volumeDown.IsEnabled = _canAdjust && (standby || state!.Volume is > 0 and <= 50);
        AutomationProperties.SetItemStatus(_volumeUp, standby ? standbyStatus : string.Empty);
        AutomationProperties.SetItemStatus(_volumeDown, standby ? standbyStatus : string.Empty);
        UpdateLevels(_volumeLevels, state?.Volume, 0, 50);
        UpdateLevels(_subwooferLevels, state?.Subwoofer, -4, 4);
        _refresh.IsEnabled = connected && !busy && !closing;
        _disconnect.IsEnabled = connected && !busy && !closing;
        _cancel.IsEnabled = canCancel && !closing;
        RefreshPresentation();

        void UpdateLevels(Dictionary<int, ConfirmedToggleMenuItem> levels, int? confirmed, int minimum, int maximum)
        {
            bool valid = confirmed is int value && value >= minimum && value <= maximum;
            foreach ((int level, ConfirmedToggleMenuItem item) in levels)
            {
                item.ConfirmedValue = valid ? confirmed == level : null;
                item.IsEnabled = _canAdjust && valid;
                AutomationProperties.SetItemStatus(item, state is null ? unknownStatus
                    : !valid ? Localization.Text("Readout.Unknown") : standby ? standbyStatus
                    : item.IsChecked ? Localization.Text("Tray.SelectedState") : string.Empty);
            }
        }
    }

    private void RefreshPresentation()
    {
        TrayPresentation presentation = TrayPresentation.Create(_connected, _state, _busy,
            Localization.Text, Localization.Culture);
        SetLabel(_status, presentation.Status);
        SetLabel(_volume, presentation.VolumeLabel);
        SetLabel(_subwoofer, presentation.SubwooferLabel);
        SetLabel(_inputs, presentation.InputLabel);
        SetLabel(_brightness, TrayPresentation.FormatBrightnessLabel(_connected, _state, Localization.Text));
        _icon.Text = presentation.Tooltip;
    }

    private static void SetLabel(MenuItem item, string label)
    {
        item.Header = label;
        AutomationProperties.SetName(item, label);
    }

    internal void Notify(string message, bool isError)
    {
        if (_disposed)
            return;
        _icon.ShowBalloonTip(5000, "Yamahama", message.Length > 250 ? message[..247] + "…" : message,
            isError ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    internal MenuItem CommandItem(SoundbarCommand command) => _commands[command];

    private static string CommandKey(SoundbarCommand command) => command switch
    {
        SoundbarCommand.SubwooferUp => "Command.SubwooferUp",
        SoundbarCommand.SubwooferDown => "Command.SubwooferDown",
        SoundbarCommand.InputTv => "Command.InputTv",
        SoundbarCommand.InputOptical => "Command.InputOptical",
        SoundbarCommand.InputBluetooth => "Command.InputBluetooth",
        SoundbarCommand.InputAnalog => "Command.InputAnalog",
        SoundbarCommand.Stereo => "Command.Stereo",
        SoundbarCommand.Standard => "Command.Standard",
        SoundbarCommand.Movie3D => "Command.Movie3D",
        SoundbarCommand.Game => "Command.Game",
        SoundbarCommand.IndicatorsBright => "Command.IndicatorsBright",
        SoundbarCommand.IndicatorsDim => "Command.IndicatorsDim",
        SoundbarCommand.IndicatorsOff => "Command.IndicatorsOff",
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    private static bool Matches(SoundbarCommand command, SoundbarState state) => command switch
    {
        SoundbarCommand.InputTv => state.Input == SoundbarInput.Tv,
        SoundbarCommand.InputOptical => state.Input == SoundbarInput.Optical,
        SoundbarCommand.InputBluetooth => state.Input == SoundbarInput.Bluetooth,
        SoundbarCommand.InputAnalog => state.Input == SoundbarInput.Analog,
        SoundbarCommand.Stereo => state.Mode == SoundMode.Stereo,
        SoundbarCommand.Standard => state.Mode == SoundMode.Standard,
        SoundbarCommand.Movie3D => state.Mode == SoundMode.Movie3D,
        SoundbarCommand.Game => state.Mode == SoundMode.Game,
        SoundbarCommand.IndicatorsBright => state.Brightness == IndicatorBrightness.Bright,
        SoundbarCommand.IndicatorsDim => state.Brightness == IndicatorBrightness.Dim,
        SoundbarCommand.IndicatorsOff => state.Brightness == IndicatorBrightness.Off,
        _ => false
    };

    private static bool IsSelectionKnown(SoundbarCommand command, SoundbarState state) => command switch
    {
        SoundbarCommand.InputTv or SoundbarCommand.InputOptical or SoundbarCommand.InputBluetooth or SoundbarCommand.InputAnalog
            => Enum.IsDefined(state.Input),
        SoundbarCommand.Stereo or SoundbarCommand.Standard or SoundbarCommand.Movie3D or SoundbarCommand.Game
            => Enum.IsDefined(state.Mode) && state.Mode != SoundMode.Unknown,
        SoundbarCommand.IndicatorsBright or SoundbarCommand.IndicatorsDim or SoundbarCommand.IndicatorsOff
            => Enum.IsDefined(state.Brightness),
        _ => false
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _canAdjust = false;
        foreach (MenuItem item in _labels.Keys.Concat(_volumeLevels.Values).Concat(_subwooferLevels.Values))
            item.IsEnabled = false;
        Menu.IsOpen = false;
        Menu.Items.Clear();
        _icon.Visible = false;
        _icon.Dispose();
        _ownedIcon.Dispose();
    }
}
