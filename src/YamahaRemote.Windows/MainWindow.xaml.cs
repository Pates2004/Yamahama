using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using YamahaRemote.Core;

namespace YamahaRemote.Windows;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand IncreaseVolumeCommand = new(Localization.Text("Command.IncreaseVolume"), nameof(IncreaseVolumeCommand), typeof(MainWindow));
    public static readonly RoutedUICommand DecreaseVolumeCommand = new(Localization.Text("Command.DecreaseVolume"), nameof(DecreaseVolumeCommand), typeof(MainWindow));
    public static readonly RoutedUICommand IncreaseSubwooferCommand = new(Localization.Text("Command.IncreaseSubwoofer"), nameof(IncreaseSubwooferCommand), typeof(MainWindow));
    public static readonly RoutedUICommand DecreaseSubwooferCommand = new(Localization.Text("Command.DecreaseSubwoofer"), nameof(DecreaseSubwooferCommand), typeof(MainWindow));

    private static CultureInfo DisplayCulture => Localization.Culture;
    private const string ManualUrl = "https://manual.yamaha.com/av/22/src30a/en-US/";
    private static string UncertainEffect => Localization.Text("Operation.UncertainEffect");
    private readonly ISoundbarController _controller;
    private readonly Action<string> _copySupportAddress;
    private readonly Action<string> _showShutdownWarning;
    private readonly string? _preferencesPath;
    private readonly HashSet<ComboBox> _manualSelectionDrafts = [];
    private bool _synchronizingSettings;
    private bool _applyingPreferences;
    private readonly (ControlRow Key, int GridRow, CheckBox Toggle)[] _controlRows;
    private bool _applyingRowVisibility;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private UiPreferences _uiPreferences = new();
    private string? _preferenceLoadWarning;
    private CancellationTokenSource? _operationCancellation;
    private Task _operationTask = Task.CompletedTask;
    private SoundbarState? _lastState;
    private SoundbarIdentity? _identity;
    private DiscoveredSoundbar? _lastDevice;
    private DateTimeOffset _unconfirmedThrough = DateTimeOffset.MinValue;
    private long _connectionGeneration;
    private string? _connectionLossMessage;
    private string? _shutdownOperationWarning;
    private bool _initialized;
    private bool _sessionConnected;
    private bool _stateIsCurrent;
    private bool _busy;
    private bool _canCancel;
    private bool _mutationInProgress;
    private bool _closing;
    private bool _allowClose;
    private bool _exitRequested;
    private bool _checkingExit;

    public MainWindow() : this(new BluetoothSoundbarController())
    {
    }

    public MainWindow(ISoundbarController controller, Action<string>? copySupportAddress = null, string? preferencesPath = null)
        : this(controller, copySupportAddress, preferencesPath, new WindowsStartupRegistration(configurationPath: preferencesPath))
    {
    }

    internal MainWindow(ISoundbarController controller, Action<string>? copySupportAddress, string? preferencesPath,
        IStartupRegistration startupRegistration, Func<string, string, bool>? confirm = null,
        Action<string>? announceState = null,
        TimeProvider? powerProtectionClock = null, Action<string>? showShutdownWarning = null,
        IHotkeyBackend? hotkeyBackend = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        _hotkeyBackend = hotkeyBackend;
        _showShutdownWarning = showShutdownWarning ?? (message => MessageBox.Show(this,
            message, Localization.Text("Shutdown.Title"), MessageBoxButton.OK, MessageBoxImage.Warning));
        _powerProtectionPolicy = new(powerProtectionClock ?? TimeProvider.System);
        _powerProtectionTimer.Tick += OnPowerProtectionTick;
        _announceState = announceState;
        _copySupportAddress = copySupportAddress ?? Clipboard.SetText;
        _preferencesPath = preferencesPath;
        _startupRegistration = startupRegistration;
        _confirmSettingsAction = confirm ?? ((message, title) => MessageBox.Show(GetDialogOwner(), message, title,
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
        Localization.SetLanguage(Localization.CurrentLanguage);
        InitializeComponent();
        InitializeSectionViews();
        _controlRows =
        [
            (ControlRow.Power, 0, ShowPowerRowItem),
            (ControlRow.Input, 1, ShowInputRowItem),
            (ControlRow.Volume, 2, ShowVolumeRowItem),
            (ControlRow.Mute, 3, ShowMuteRowItem),
            (ControlRow.Subwoofer, 4, ShowSubwooferRowItem),
            (ControlRow.SoundMode, 6, ShowSoundModeRowItem),
            (ControlRow.ClearVoice, 7, ShowClearVoiceRowItem),
            (ControlRow.BassExtension, 8, ShowBassRowItem),
            (ControlRow.IndicatorBrightness, 9, ShowBrightnessRowItem)
        ];
        _controller.StateChanged += OnStateChanged;
        _controller.ConnectionLost += OnConnectionLost;
        _initialized = true;
        RefreshLocalizedContent();
        UpdateAvailability();
        LoadUiPreferences();
        ApplyExtendedPreferences();
        EnsureTray();
        ApplyStartupPreferences();
        RefreshSavedDevices();
        UpdateApplicationChoices();
        Localization.LanguageChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, EventArgs eventArgs)
    {
        if (!_initialized || _closing)
            return;
        RefreshLocalizedContent();
        _tray?.RefreshLabels(_uiPreferences.TextScale);
        UpdateTrayState();
        SetStatus(Localization.Text("Settings.LanguageChanged"));
    }

    private void RefreshLocalizedContent()
    {
        Language = XmlLanguage.GetLanguage(Localization.Culture.Name);
        IncreaseVolumeCommand.Text = Localization.Text("Command.IncreaseVolume");
        DecreaseVolumeCommand.Text = Localization.Text("Command.DecreaseVolume");
        IncreaseSubwooferCommand.Text = Localization.Text("Command.IncreaseSubwoofer");
        DecreaseSubwooferCommand.Text = Localization.Text("Command.DecreaseSubwoofer");
        FirmwareGuideReadout.Text = SupportResources.ReadGuide(SupportGuide.Firmware);
        TouchPanelGuideReadout.Text = SupportResources.ReadGuide(SupportGuide.TouchPanel);
        ResetGuideReadout.Text = SupportResources.ReadGuide(SupportGuide.Reset);
        foreach (DeviceEntry entry in DevicesList.Items)
            entry.RefreshDisplayName();
        foreach (SavedDeviceEntry entry in SavedDevicesList.Items)
            entry.RefreshDisplayName();
        RenderReadouts();
        ApplyBehaviorPreferences();
        if (_hotkeyFields.Count > 0)
        {
            bool wasApplying = _applyingPreferences;
            _applyingPreferences = true;
            try { PopulateHotkeyControls(); }
            finally { _applyingPreferences = wasApplying; }
        }
        UpdateProfileControls();
    }

    private bool Connected => _initialized && _sessionConnected && _controller.IsConnected;
    private bool CanOperate => !_closing && !_busy && Connected;
    private bool CanAdjust => CanOperate && _stateIsCurrent && _lastState is not null;

    private async void OnWindowLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_preferenceLoadWarning is not null)
            SetStatus(_preferenceLoadWarning, true);
        else
            ConnectionSummary.Focus();
        await RunStartupDeviceAsync();
    }

    private async void OnSearch(object sender, RoutedEventArgs eventArgs)
    {
        if (Connected)
            return;
        ulong? selectedAddress = (DevicesList.SelectedItem as DeviceEntry)?.Device.Address;
        await ExecuteAsync(Localization.Text("Search.Pending"), async token =>
        {
            IReadOnlyList<DiscoveredSoundbar> devices = await _controller.DiscoverAsync(token);
            token.ThrowIfCancellationRequested();
            DeviceEntry[] entries = devices.GroupBy(device => device.Address)
                .Select(group => group.OrderByDescending(device => device.SignalStrength).First())
                .OrderByDescending(device => device.SignalStrength)
                .Select(device => new DeviceEntry(device)).ToArray();
            DevicesList.ItemsSource = entries;
            DevicesList.SelectedItem = entries.FirstOrDefault(entry => entry.Device.Address == selectedAddress) ?? entries.FirstOrDefault();
            return entries.Length == 0
                ? Localization.Text("Search.Empty")
                : Localization.Text("Search.Complete", entries.Length);
        }, FocusSearchResults);
    }

    private async void OnConnect(object sender, RoutedEventArgs eventArgs) => await ConnectSelectedAsync(true);

    private async Task ConnectSelectedAsync(bool remember)
    {
        if (_closing || _busy || Connected || SelectedDevice is not DiscoveredSoundbar selected)
            return;
        Interlocked.Increment(ref _connectionGeneration);
        _sessionConnected = false;
        _stateIsCurrent = false;
        _powerProtectionUncertain = false;
        _lastState = null;
        _feedbackState = null;
        _identity = null;
        _lastDevice = selected;
        _unconfirmedThrough = DateTimeOffset.MinValue;

        InputChoice.SelectedIndex = -1;
        ModeChoice.SelectedIndex = -1;
        BrightnessChoice.SelectedIndex = -1;
        RenderReadouts();
        await ExecuteAsync(Localization.Text("Connection.Pending", GetDeviceName(selected)), async token =>
        {
            try
            {
                SoundbarIdentity identity = await _controller.ConnectAsync(selected, token);
                token.ThrowIfCancellationRequested();
                if (identity.ModelCode != 0x27)
                    throw new LocalizedOperationException("Bluetooth.WrongModel", identity.Model);
                if (!_controller.IsConnected)
                    throw new LocalizedOperationException("Connection.Unconfirmed");
                _identity = identity;
                _sessionConnected = true;
                CaptureFreshState(null);
                return Localization.Text("Connection.Complete", GetDeviceName(selected)) + " "
                    + (remember ? RememberConnectedDevice() : Localization.Text("Device.OneOffSession"));
            }
            catch (Exception connectionError)
            {
                _sessionConnected = false;
                _stateIsCurrent = false;
                try
                {
                    await _controller.DisconnectAsync();
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException(connectionError, cleanupError);
                }
                throw;
            }
        }, FocusConnectedControls, invalidateOnFailure: true);
    }

    private async void OnDisconnect(object sender, RoutedEventArgs eventArgs)
    {
        if (_closing || _busy)
            return;
        _powerProtectionCancellation?.Cancel();
        await ExecuteAsync(Localization.Text("Connection.Disconnecting"), async _ =>
        {
            Interlocked.Increment(ref _connectionGeneration);
            _sessionConnected = false;
            InvalidateReadout();
            await _controller.DisconnectAsync();
            return Localization.Text("Connection.Disconnected");
        }, FocusDeviceSearch, canCancel: false, invalidateOnFailure: true);
    }

    private async void OnRefresh(object sender, RoutedEventArgs eventArgs)
    {
        await ExecuteAsync(Localization.Text("Readout.Refreshing"), async token =>
        {
            SoundbarState? before = _controller.State;
            await _controller.RefreshAsync(token);
            token.ThrowIfCancellationRequested();
            CaptureFreshState(before);
            return Localization.Text("Readout.Refreshed");
        }, requiresConnection: true, invalidateOnFailure: true);
    }

    private async void OnForgetDevice(object sender, RoutedEventArgs eventArgs)
    {
        if (_closing || _busy)
            return;
        MessageBoxResult confirmation = MessageBox.Show(this,
            Localization.Text("Device.ForgetQuestion"),
            Localization.Text("Device.ForgetTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
            return;
        _powerProtectionCancellation?.Cancel();
        await ExecuteAsync(Localization.Text("Device.Forgetting"), async _ =>
        {
            Interlocked.Increment(ref _connectionGeneration);
            _sessionConnected = false;
            InvalidateReadout();
            await _controller.DisconnectAsync();
            _lastState = null;
            _identity = null;
            _lastDevice = null;
            _stateIsCurrent = false;
            _unconfirmedThrough = DateTimeOffset.MinValue;

            DevicesList.ItemsSource = null;
            DevicesList.SelectedIndex = -1;
            InputChoice.SelectedIndex = -1;
            ModeChoice.SelectedIndex = -1;
            BrightnessChoice.SelectedIndex = -1;
            return Localization.Text("Device.Forgotten");
        }, FocusDeviceSearch, canCancel: false, invalidateOnFailure: true);
    }

    private async void OnToggleSetting(object sender, RoutedEventArgs eventArgs)
    {
        if (!_initialized || _synchronizingSettings || sender is not CheckBox toggle)
            return;
        bool requested = toggle.IsChecked == true;
        SoundbarCommand? command = toggle.Name switch
        {
            nameof(PowerToggle) => requested ? SoundbarCommand.PowerOn : SoundbarCommand.PowerOff,
            nameof(MuteToggle) => requested ? SoundbarCommand.MuteOn : SoundbarCommand.MuteOff,
            nameof(ClearVoiceToggle) => requested ? SoundbarCommand.ClearVoiceOn : SoundbarCommand.ClearVoiceOff,
            nameof(BassToggle) => requested ? SoundbarCommand.BassExtensionOn : SoundbarCommand.BassExtensionOff,
            _ => null
        };
        SynchronizeSettingControls();
        if (CanAdjust && command is not null)
            await SendCommandAsync(command.Value);
    }

    private async void OnApplyInput(object sender, RoutedEventArgs eventArgs) => await SendSelectedCommandAsync(InputChoice);
    private async void OnApplyMode(object sender, RoutedEventArgs eventArgs) => await SendSelectedCommandAsync(ModeChoice);
    private async void OnApplyBrightness(object sender, RoutedEventArgs eventArgs) => await SendSelectedCommandAsync(BrightnessChoice);

    private Task SendSelectedCommandAsync(ComboBox choice)
    {
        if (choice.SelectedItem is not ComboBoxItem { Tag: SoundbarCommand command })
            return Task.CompletedTask;
        _manualSelectionDrafts.Remove(choice);
        SynchronizeSettingControls();
        return SendCommandAsync(command);
    }

    private Task SendCommandAsync(SoundbarCommand command)
    {
        string description = GetCommandDescription(command);
        return ExecuteAsync(Localization.Text("Command.Pending", description), async token =>
        {
            SoundbarState? before = _controller.State;
            await _controller.SendAsync(command, token);
            token.ThrowIfCancellationRequested();
            CaptureFreshState(before);
            return Localization.Text("Command.Confirmed", description);
        }, requiresConnection: true, changesState: true, invalidateOnFailure: true,
            requiresPower: command is not (SoundbarCommand.PowerOn or SoundbarCommand.PowerOff));
    }

    private Task SetVolumeAsync(int volume)
    {
        return ExecuteAsync(Localization.Text("Volume.Pending", volume), async token =>
        {
            SoundbarState? before = _controller.State;
            await _controller.SetVolumeAsync(volume, token);
            token.ThrowIfCancellationRequested();
            CaptureFreshState(before);

            return Localization.Text("Volume.Confirmed", _lastState!.Volume);
        }, requiresConnection: true, changesState: true, invalidateOnFailure: true, requiresPower: true);
    }

    private async void OnIncreaseVolume(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        if (CanAdjust && RejectStandbyEdit())
            return;
        if (CanAdjust && _lastState is { Volume: >= 0 and < 50 } state)
            await SetVolumeAsync(state.Volume + 1);
    }

    private async void OnDecreaseVolume(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        if (CanAdjust && RejectStandbyEdit())
            return;
        if (CanAdjust && _lastState is { Volume: > 0 and <= 50 } state)
            await SetVolumeAsync(state.Volume - 1);
    }

    private async void OnIncreaseSubwoofer(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        if (CanAdjust && RejectStandbyEdit())
            return;
        if (CanAdjust && _lastState is { Subwoofer: >= -4 and < 4 })
            await SendCommandAsync(SoundbarCommand.SubwooferUp);
    }

    private async void OnDecreaseSubwoofer(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        if (CanAdjust && RejectStandbyEdit())
            return;
        if (CanAdjust && _lastState is { Subwoofer: > -4 and <= 4 })
            await SendCommandAsync(SoundbarCommand.SubwooferDown);
    }

    private void CanIncreaseVolume(object sender, CanExecuteRoutedEventArgs eventArgs) => eventArgs.CanExecute = CanAdjust && (IsSoundbarInStandby || _lastState is { Volume: >= 0 and < 50 });
    private void CanDecreaseVolume(object sender, CanExecuteRoutedEventArgs eventArgs) => eventArgs.CanExecute = CanAdjust && (IsSoundbarInStandby || _lastState is { Volume: > 0 and <= 50 });
    private void CanIncreaseSubwoofer(object sender, CanExecuteRoutedEventArgs eventArgs) => eventArgs.CanExecute = CanAdjust && (IsSoundbarInStandby || _lastState is { Subwoofer: >= -4 and < 4 });
    private void CanDecreaseSubwoofer(object sender, CanExecuteRoutedEventArgs eventArgs) => eventArgs.CanExecute = CanAdjust && (IsSoundbarInStandby || _lastState is { Subwoofer: > -4 and <= 4 });

    private Task ExecuteAsync(string pendingMessage, Func<CancellationToken, Task<string>> operation,
        Action? successFocus = null, bool requiresConnection = false, bool changesState = false,
        bool canCancel = true, bool invalidateOnFailure = false, bool requiresPower = false)
    {
        if (_closing || _busy)
            return Task.CompletedTask;
        if (requiresConnection && !Connected)
        {
            InvalidateReadout();
            SetStatus(Localization.Text("Connection.Required"), true);
            UpdateAvailability();
            return Task.CompletedTask;
        }
        if (requiresPower && RejectStandbyEdit())
            return Task.CompletedTask;
        IInputElement? previousFocus = Keyboard.FocusedElement;
        CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _operationCancellation = cancellation;
        _connectionLossMessage = null;
        _busy = true;
        _canCancel = canCancel;
        _mutationInProgress = changesState;
        UpdateAvailability();
        SetStatus(pendingMessage);
        _operationTask = CompleteOperationAsync(operation, cancellation, successFocus, previousFocus,
            _focusInteractionGeneration, _sectionFocusGeneration, changesState, invalidateOnFailure, requiresPower);
        return _operationTask;
    }

    private async Task CompleteOperationAsync(Func<CancellationToken, Task<string>> operation,
        CancellationTokenSource cancellation, Action? successFocus, IInputElement? previousFocus,
        long interactionGeneration, long sectionGeneration, bool changesState, bool invalidateOnFailure, bool requiresPower)
    {
        bool succeeded = false;
        bool important = false;
        string message;
        try
        {
            await _powerProtectionTask;
            cancellation.Token.ThrowIfCancellationRequested();
            if (requiresPower && IsSoundbarInStandby)
            {
                message = Localization.Text("State.PowerOffEditBlocked");
                important = true;
                invalidateOnFailure = false;
                changesState = false;
            }
            else
            {
                message = await operation(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (_connectionLossMessage is not null)
                    throw new InvalidOperationException(_connectionLossMessage);
                succeeded = true;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            important = changesState || _connectionLossMessage is not null;
            message = _connectionLossMessage ?? Localization.Text("Operation.Cancelled");
            if (changesState)
                message += UncertainEffect;
        }
        catch (Exception exception)
        {
            message = Localization.Text("Operation.Unconfirmed") + DescribeError(exception);
            important = true;
            if (changesState)
                message += UncertainEffect;
        }
        finally
        {
            _operationCancellation = null;
            cancellation.Dispose();
            _busy = false;
            _canCancel = false;
            _mutationInProgress = false;
        }
        if (_closing)
        {
            if (!succeeded && changesState)
                _shutdownOperationWarning = message;
            return;
        }
        if (!succeeded && invalidateOnFailure)
            InvalidateReadout();
        if (!_controller.IsConnected)
            _sessionConnected = false;
        RenderReadouts();
        UpdateAvailability();
        if (_powerProtectionUncertain && !Connected)
        {
            if (succeeded || !changesState)
                message += UncertainEffect;
            important = true;
        }
        bool restoreFocus = interactionGeneration == _focusInteractionGeneration
            && sectionGeneration == _sectionFocusGeneration && IsVisible && _globalHotkeyDepth == 0;
        SetStatus(message, focus: important && restoreFocus, important: important);
        if (!succeeded)
            return;
        if (restoreFocus)
        {
            if (successFocus is not null)
                successFocus();
            else if (previousFocus is UIElement { IsEnabled: true, IsVisible: true } element)
                element.Focus();
            else
                FocusOperationResult();
        }
        AnnounceConfirmedState();
    }

    private void OnCancelOperation(object sender, RoutedEventArgs eventArgs)
    {
        if (!_busy || !_canCancel || _operationCancellation is null)
            return;
        _canCancel = false;
        UpdateAvailability();
        SetStatus(Localization.Text("Operation.Cancelling") + (_mutationInProgress ? UncertainEffect : string.Empty), _mutationInProgress);
        CancelCurrentOperation();
    }

    private void CancelCurrentOperation()
    {
        try
        {
            _operationCancellation?.Cancel();
        }
        catch (Exception exception)
        {
            SetStatus(Localization.Text("Operation.CancelFailed") + DescribeError(exception)
                + (_mutationInProgress ? UncertainEffect : string.Empty), true);
        }
    }

    private void OnStateChanged(SoundbarState state)
    {
        long generation = Interlocked.Read(ref _connectionGeneration);
        DispatchControllerEvent(() =>
        {
            if (generation == Interlocked.Read(ref _connectionGeneration) && Connected && ReferenceEquals(state, _controller.State))
                ApplyState(state);
        });
    }

    private void OnConnectionLost(string reason)
    {
        long generation = Interlocked.Read(ref _connectionGeneration);
        DispatchControllerEvent(() =>
        {
            if (generation != Interlocked.Read(ref _connectionGeneration) || !_sessionConnected || _controller.IsConnected)
                return;
            Interlocked.Increment(ref _connectionGeneration);
            _connectionLossMessage = Localization.Text("Connection.Lost") + reason;
            _sessionConnected = false;
            InvalidateReadout();
            CancelCurrentOperation();
            UpdateAvailability();
            SetStatus(_connectionLossMessage
                + (_mutationInProgress || _powerProtectionMutationInProgress || _powerProtectionUncertain ? UncertainEffect : string.Empty), true);
        });
    }

    private void DispatchControllerEvent(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            {
                if (_closing)
                    return;
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    _stateIsCurrent = false;
                    UpdateAvailability();
                    SetStatus(Localization.Text("Readout.ProcessingFailed") + DescribeError(exception, false), true);
                }
            }));
        }
        catch (InvalidOperationException exception)
        {
            Trace.WriteLine(exception);
        }
    }

    private void CaptureFreshState(SoundbarState? previous)
    {
        SoundbarState? current = _controller.State;
        if (!Connected || current is null || (previous is not null && current.ReceivedAt <= previous.ReceivedAt))
            throw new LocalizedOperationException("Readout.FreshStateRequired");
        ApplyState(current);
    }

    private void ApplyState(SoundbarState state)
    {
        if (_lastState is not null && state.ReceivedAt < _lastState.ReceivedAt)
            return;
        _lastState = state;
        _stateIsCurrent = Connected && state.ReceivedAt > _unconfirmedThrough;
        if (_stateIsCurrent && !_powerProtectionMutationInProgress)
            _powerProtectionUncertain = false;

        RenderReadouts();
        UpdateAvailability();
        AnnounceConfirmedState(deviceUpdate: !_powerProtectionMutationInProgress);
    }

    private void InvalidateReadout()
    {
        _stateIsCurrent = false;
        _feedbackState = null;
        if (_lastState is not null && _lastState.ReceivedAt > _unconfirmedThrough)
            _unconfirmedThrough = _lastState.ReceivedAt;
        if (_controller.State is SoundbarState queuedState && queuedState.ReceivedAt > _unconfirmedThrough)
            _unconfirmedThrough = queuedState.ReceivedAt;
        RenderReadouts();
    }

    private void RenderReadouts()
    {
        bool current = Connected && _stateIsCurrent;
        string freshness = _lastState is null
            ? Localization.Text("Readout.None")
            : current
                ? Localization.Text("Readout.Current") + FormatTime(_lastState.ReceivedAt)
                : Localization.Text("Readout.Stale") + FormatTime(_lastState.ReceivedAt);
        ReadoutStatus.Text = freshness;
        InformationStatus.Text = freshness;
        ConnectionSummary.Text = Connected
            ? Localization.Text("Connection.SummaryConnected", GetDeviceName(_lastDevice)) + (current ? Localization.Text("Connection.CurrentReading") : Localization.Text("Connection.UnconfirmedReading"))
            : Localization.Text("Connection.SummaryDisconnected");
        AutomationProperties.SetName(ConnectionSummary, ConnectionSummary.Text);
        AutomationProperties.SetName(ReadoutStatus, freshness);
        string prefix = _lastState is not null && !current ? Localization.Text("Readout.LastPrefix") : string.Empty;
        PowerReadout.Text = _lastState is null ? Localization.Text("Readout.Unknown") : prefix + (_lastState.Power ? Localization.Text("State.On") : Localization.Text("Feedback.PowerOff"));
        InputReadout.Text = _lastState is null ? Localization.Text("Readout.Unknown") : prefix + FormatInput(_lastState.Input);
        VolumeReadout.Text = _lastState is null ? Localization.Text("Readout.UnknownFeminine") : prefix + FormatLevel(_lastState.Volume, 0, 50);
        MuteReadout.Text = _lastState is null ? Localization.Text("Readout.Unknown") : prefix + FormatSwitch(_lastState.Muted);
        SubwooferReadout.Text = _lastState is null ? Localization.Text("Readout.Unknown") : prefix + FormatLevel(_lastState.Subwoofer, -4, 4);
        ModeReadout.Text = _lastState is null ? Localization.Text("Readout.UnknownMasculine") : prefix + FormatMode(_lastState.Mode);
        ClearVoiceReadout.Text = _lastState is null ? Localization.Text("Readout.Unknown") : prefix + FormatSwitch(_lastState.ClearVoice);
        BassReadout.Text = _lastState is null ? Localization.Text("Readout.Unknown") : prefix + FormatSwitch(_lastState.BassExtension);
        BrightnessReadout.Text = _lastState is null ? Localization.Text("Readout.UnknownFeminine") : prefix + FormatBrightness(_lastState.Brightness);
        string identityPrefix = !Connected && _identity is not null ? Localization.Text("Readout.LastPrefix") : string.Empty;
        DeviceReadout.Text = _lastDevice is null ? Localization.Text("Readout.Unknown") : GetDeviceName(_lastDevice) + ", " + FormatAddress(_lastDevice.Address);
        ModelReadout.Text = _identity is null ? Localization.Text("Readout.UnknownMasculine") : identityPrefix + KnownText(_identity.Model);
        FirmwareReadout.Text = _identity is null ? Localization.Text("Readout.UnknownFeminine") : identityPrefix + KnownText(_identity.Firmware);
        FirmwareDeviceReadout.Text = _identity is null
            ? Localization.Text("Guide.NoDeviceReading")
            : identityPrefix + KnownText(_identity.Model) + Localization.Text("Guide.FirmwareSeparator") + KnownText(_identity.Firmware);
        LastResponseReadout.Text = _lastState is null ? Localization.Text("Readout.NoResponse") : FormatTime(_lastState.ReceivedAt);
        UpdateReadoutPresentation();
        SynchronizeSettingControls();
    }

    private void SynchronizeSettingControls()
    {
        if (_synchronizingSettings)
            return;
        _synchronizingSettings = true;
        try
        {
            SoundbarState? state = Connected && _stateIsCurrent ? _lastState : null;
            PowerToggle.IsChecked = state?.Power;
            MuteToggle.IsChecked = state?.Muted;
            ClearVoiceToggle.IsChecked = state?.ClearVoice;
            BassToggle.IsChecked = state?.BassExtension;
            if (state is null || !state.Power)
                _manualSelectionDrafts.Clear();
            foreach (ComboBox choice in new[] { InputChoice, ModeChoice, BrightnessChoice })
            {
                if (!_uiPreferences.AutoApplySelections && _manualSelectionDrafts.Contains(choice))
                    continue;
                SoundbarCommand? command = GetConfirmedSelection(choice);
                choice.SelectedItem = choice.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(item => command is not null && item.Tag is SoundbarCommand itemCommand && itemCommand == command);
            }
        }
        finally
        {
            _synchronizingSettings = false;
        }
    }

    private SoundbarCommand? GetConfirmedSelection(ComboBox choice)
    {
        if (!Connected || !_stateIsCurrent || _lastState is not SoundbarState state)
            return null;
        if (ReferenceEquals(choice, InputChoice))
            return state.Input switch
            {
                SoundbarInput.Tv => SoundbarCommand.InputTv,
                SoundbarInput.Optical => SoundbarCommand.InputOptical,
                SoundbarInput.Bluetooth => SoundbarCommand.InputBluetooth,
                SoundbarInput.Analog => SoundbarCommand.InputAnalog,
                _ => null
            };
        if (ReferenceEquals(choice, ModeChoice))
            return state.Mode switch
            {
                SoundMode.Stereo => SoundbarCommand.Stereo,
                SoundMode.Standard => SoundbarCommand.Standard,
                SoundMode.Movie3D => SoundbarCommand.Movie3D,
                SoundMode.Game => SoundbarCommand.Game,
                _ => null
            };
        return state.Brightness switch
        {
            IndicatorBrightness.Bright => SoundbarCommand.IndicatorsBright,
            IndicatorBrightness.Dim => SoundbarCommand.IndicatorsDim,
            IndicatorBrightness.Off => SoundbarCommand.IndicatorsOff,
            _ => null
        };
    }

    private void UpdateAvailability()
    {
        if (!_initialized)
            return;
        bool available = !_busy && !_closing;
        SearchButton.IsEnabled = available && !Connected;
        DevicesList.IsEnabled = available && !Connected;
        SavedDevicesList.IsEnabled = available;
        ConnectButton.IsEnabled = available && !Connected && SelectedDevice is not null;
        ConnectOnceMenuItem.IsEnabled = ConnectButton.IsEnabled;
        AddDeviceButton.IsEnabled = CanAdjust && _lastDevice is not null && !_uiPreferences.SavedDevices.Any(device => device.Address == _lastDevice.Address);
        RemoveSavedDeviceButton.IsEnabled = available && SavedDevicesList.SelectedItem is SavedDeviceEntry;
        SettingsLanguageChoice.IsEnabled = available;
        SettingsThemeChoice.IsEnabled = available;
        DisconnectButton.IsEnabled = available && (Connected || _controller.IsConnected);
        ForgetButton.IsEnabled = available && (_lastDevice is not null || DevicesList.Items.Count > 0 || _controller.IsConnected);
        RefreshButton.IsEnabled = CanOperate;
        CancelButton.IsEnabled = !_closing && _busy && _canCancel;
        PowerToggle.IsEnabled = CanAdjust;
        InputChoice.IsEnabled = CanAdjust;
        ModeChoice.IsEnabled = CanAdjust;
        BrightnessChoice.IsEnabled = CanAdjust;
        ApplyInputButton.IsEnabled = CanAdjust && InputChoice.SelectedItem is ComboBoxItem;
        ApplyModeButton.IsEnabled = CanAdjust && ModeChoice.SelectedItem is ComboBoxItem;
        ApplyBrightnessButton.IsEnabled = CanAdjust && BrightnessChoice.SelectedItem is ComboBoxItem;
        MuteToggle.IsEnabled = CanAdjust;
        ClearVoiceToggle.IsEnabled = CanAdjust;
        BassToggle.IsEnabled = CanAdjust;
        AutoApplySelectionsCheckBox.IsEnabled = available;
        ShowOperationMessagesCheckBox.IsEnabled = available;
        ValueDisplayModeChoice.IsEnabled = available;
        AnnounceStateChangesCheckBox.IsEnabled = available;
        AnnounceExternalStateChangesCheckBox.IsEnabled = available;
        GlobalHotkeysCheckBox.IsEnabled = available;
        HotkeyRows.IsEnabled = available;
        ResetHotkeysSettingsButton.IsEnabled = available;
        CloseToTrayCheckBox.IsEnabled = available;
        SettingsTextScaleChoice.IsEnabled = available;
        StartWithWindowsCheckBox.IsEnabled = available;
        StartupDeviceChoice.IsEnabled = available && _uiPreferences.SavedDevices.Length > 0;
        PowerOnAtStartupCheckBox.IsEnabled = available && SettingsPreferences.StartupDeviceAddress is not null;
        AutoConnectAtStartupCheckBox.IsEnabled = available;
        PowerOffOnExitCheckBox.IsEnabled = available;
        KeepPoweredOnCheckBox.IsEnabled = !_closing;
        ConfirmExitCheckBox.IsEnabled = available;
        VolumeControlModeChoice.IsEnabled = available;
        SubwooferControlModeChoice.IsEnabled = available;
        UpdateLevelPresentation();
        foreach (Button reset in new[] { ResetAppearanceSettingsButton, ResetMainScreenSettingsButton,
            ResetControlsSettingsButton, ResetStartupSettingsButton, ResetClosingSettingsButton })
            reset.IsEnabled = available;
        ResetApplicationButton.IsEnabled = available;
        ResetSoundButton.IsEnabled = available;
        FactoryResetGuideButton.IsEnabled = available;
        UpdateProfileControls();
        UpdateStandbyAccessibility();
        UpdateTrayState();
        UpdatePowerProtectionMonitoring();
        CommandManager.InvalidateRequerySuggested();
    }

    private void SetStatus(string message, bool focus = false, bool important = false)
    {
        _statusIsImportant = focus || important;
        StatusMessage.Text = message;
        UpdateOperationMessageVisibility();
        AutomationProperties.SetName(StatusMessage, Localization.Text("Status.MessagePrefix") + message);
        ApplySectionPreferences();
        if (!_statusIsImportant && !_uiPreferences.ShowOperationMessages)
            return;
        if (GetDialogOwner() is SectionWindow section)
        {
            section.AnnounceStatus();
            if (focus && !_closing)
                section.FocusStatus();
            return;
        }
        if (!IsVisible && !_busy && !_closing)
            _tray?.Notify(message, _statusIsImportant);
        try
        {
            AutomationPeer? peer = UIElementAutomationPeer.FromElement(StatusMessage)
                ?? UIElementAutomationPeer.CreatePeerForElement(StatusMessage);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ElementNotAvailableException or COMException)
        {
            Trace.WriteLine(exception);
        }
        if (focus && !_closing)
            FocusOperationResult();
    }

    private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_initialized || _updatingDeviceLists)
            return;
        if (DevicesList.SelectedItem is not null)
            SavedDevicesList.SelectedItem = null;
        UpdateAvailability();
    }
    private async void OnSettingSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_initialized || _synchronizingSettings || sender is not ComboBox choice)
            return;
        if (!CanAdjust || choice.SelectedItem is not ComboBoxItem { Tag: SoundbarCommand command })
        {
            SynchronizeSettingControls();
            return;
        }
        if (RejectStandbyEdit())
            return;
        if (_uiPreferences.AutoApplySelections)
        {
            if (command != GetConfirmedSelection(choice))
                await SendSelectedCommandAsync(choice);
        }
        else
        {
            _manualSelectionDrafts.Add(choice);
            UpdateAvailability();
        }
    }

    private void FocusSearchResults()
    {
        MainTabs.SelectedItem = DevicesTab;
        DevicesTab.UpdateLayout();
        if (DevicesList.SelectedItem is null)
        {
            FocusOperationResult();
            return;
        }
        DevicesList.ScrollIntoView(DevicesList.SelectedItem);
        DevicesList.UpdateLayout();
        if (DevicesList.ItemContainerGenerator.ContainerFromItem(DevicesList.SelectedItem) is ListBoxItem item)
            item.Focus();
        else
            DevicesList.Focus();
    }

    private void FocusConnectedControls()
    {
        MainTabs.SelectedItem = ControlTab;
        ControlTab.UpdateLayout();
        if (_lastState?.Power == true && VolumeSlider.IsEnabled && VolumeSlider.IsVisible)
            VolumeSlider.Focus();
        else if (_lastState?.Power == true && DecreaseVolumeButton.IsEnabled && DecreaseVolumeButton.IsVisible)
            DecreaseVolumeButton.Focus();
        else if (PowerToggle.IsEnabled && PowerToggle.IsVisible)
            PowerToggle.Focus();
        else if (!ControlRowsGrid.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)))
            FocusMainContent();
    }

    private void FocusDeviceSearch()
    {
        MainTabs.SelectedItem = DevicesTab;
        DevicesTab.UpdateLayout();
        SearchButton.Focus();
    }

    private void OnAbout(object sender, RoutedEventArgs eventArgs)
    {
        if (!_closing)
            OpenHelpWindow();
    }

    private void LoadUiPreferences()
    {
        _uiPreferences = UiPreferences.Load(out _preferenceLoadWarning, _preferencesPath);
        ApplyControlRowVisibility();
        ApplyBehaviorPreferences();
        try
        {
            ApplyTheme(_uiPreferences.Theme);
        }
        catch (Exception exception)
        {
            _preferenceLoadWarning = Localization.Text("Appearance.RestoreThemeFailed") + DescribeError(exception, false);
        }
    }

    private void OnBehaviorSettingsChanged(object sender, RoutedEventArgs eventArgs)
    {
        ChangeSettings(preferences => preferences with
        {
            AutoApplySelections = AutoApplySelectionsCheckBox.IsChecked == true,
            ShowHints = ShowHintsCheckBox.IsChecked == true,
            ShowOperationMessages = ShowOperationMessagesCheckBox.IsChecked == true,
            AnnounceStateChanges = AnnounceStateChangesCheckBox.IsChecked == true,
            AnnounceExternalStateChanges = AnnounceExternalStateChangesCheckBox.IsChecked == true
        });
    }

    private void ApplyBehaviorPreferences()
    {
        _applyingPreferences = true;
        try
        {
            AutoApplySelectionsCheckBox.IsChecked = SettingsPreferences.AutoApplySelections;
            ShowHintsCheckBox.IsChecked = SettingsPreferences.ShowHints;
            ShowOperationMessagesCheckBox.IsChecked = SettingsPreferences.ShowOperationMessages;
            ValueDisplayModeChoice.SelectedItem = ValueDisplayModeChoice.Items.Cast<ComboBoxItem>()
                .First(item => item.Tag is ValueDisplayMode mode && mode == SettingsPreferences.ValueDisplayMode);
            AnnounceStateChangesCheckBox.IsChecked = SettingsPreferences.AnnounceStateChanges;
            AnnounceExternalStateChangesCheckBox.IsChecked = SettingsPreferences.AnnounceExternalStateChanges;
            KeepPoweredOnCheckBox.IsChecked = SettingsPreferences.KeepPoweredOn;
            UpdatePowerProtectionMonitoring();
            ApplyLevelPreferences();
            UpdateReadoutPresentation();
            UpdateOperationMessageVisibility();
            UsageHints.SetEnabled(this, _uiPreferences.ShowHints);
            AutomationProperties.SetName(MainTabs, _uiPreferences.ShowHints ? Localization.Text("Tabs.Name") : string.Empty);
            AutomationProperties.SetName(SettingsTabs, _uiPreferences.ShowHints ? Localization.Text("Settings.TabsName") : string.Empty);
            ApplySectionPreferences();
            foreach (Button button in new[] { ApplyInputButton, ApplyModeButton, ApplyBrightnessButton })
                button.Visibility = _uiPreferences.AutoApplySelections ? Visibility.Collapsed : Visibility.Visible;
            foreach (ComboBox choice in new[] { InputChoice, ModeChoice, BrightnessChoice })
                UsageHints.SetText(choice, _uiPreferences.AutoApplySelections
                    ? Localization.Text("Control.ImmediateSelectionHint")
                    : Localization.Text("Control.ManualSelectionHint"));
        }
        finally
        {
            _applyingPreferences = false;
        }
    }

    private void OnControlRowVisibilitySelected(object sender, RoutedEventArgs eventArgs)
    {
        if (_applyingRowVisibility)
            return;
        ChangeSettings(preferences => preferences with
        {
            HiddenRows = _controlRows.Where(row => row.Toggle.IsChecked != true).Select(row => row.Key).ToArray()
        });
    }

    private void ApplyControlRowVisibility()
    {
        bool restoreFocus = false;
        _applyingRowVisibility = true;
        foreach (var row in _controlRows)
        {
            bool visible = !_uiPreferences.HiddenRows.Contains(row.Key);
            row.Toggle.IsChecked = !SettingsPreferences.HiddenRows.Contains(row.Key);
            foreach (UIElement element in ControlRowsGrid.Children)
            {
                if (Grid.GetRow(element) != row.GridRow)
                    continue;
                restoreFocus |= !visible && element.IsKeyboardFocusWithin;
                element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        _applyingRowVisibility = false;
        SoundSectionHeading.Visibility = _controlRows.Any(row => row.GridRow >= 6 && row.Toggle.IsChecked == true)
            ? Visibility.Visible : Visibility.Collapsed;
        AllControlsHiddenMessage.Visibility = _controlRows.All(row => row.Toggle.IsChecked != true)
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateReadoutPresentation();
        if (restoreFocus)
            FocusMainContent();
    }

#pragma warning disable WPF0001
    private void ApplyTheme(string selection)
    {
        Application.Current.ThemeMode = selection switch
        {
            "Light" => System.Windows.ThemeMode.Light,
            "Dark" => System.Windows.ThemeMode.Dark,
            _ => System.Windows.ThemeMode.System
        };
    }

#pragma warning restore WPF0001

    private void OnCopySupportAddress(object sender, RoutedEventArgs eventArgs)
    {
        if (_closing)
            return;
        string address = ReferenceEquals(sender, FirmwareDownloadsButton)
            ? SupportResources.FirmwareDownloadsAddress
            : SupportResources.YamahaSupportAddress;
        try
        {
            _copySupportAddress(address);
            SetStatus(Localization.Text("Guide.AddressCopied") + address
                + Localization.Text("Guide.NoUpdateCheckNotice"));
        }
        catch (Exception exception)
        {
            SetStatus(Localization.Text("Guide.CopyFailed") + DescribeError(exception, false)
                + Localization.Text("Guide.CopyManuallyNotice") + address, true);
        }
    }

    private void OnOpenManual(object sender, RoutedEventArgs eventArgs)
    {
        if (_closing)
            return;
        try
        {
            using Process? browser = Process.Start(new ProcessStartInfo(ManualUrl) { UseShellExecute = true });
            SetStatus(Localization.Text("Guide.ManualOpened"));
        }
        catch (Exception exception)
        {
            SetStatus(Localization.Text("Guide.ManualFailed") + DescribeError(exception, false) + Localization.Text("Guide.AddressSuffix") + ManualUrl, true);
        }
    }

    private void OnExit(object sender, RoutedEventArgs eventArgs) => RequestExit();

    internal void RequestExit()
    {
        if (_closing || _checkingExit)
            return;
        _exitRequested = true;
        try
        {
            Close();
        }
        finally
        {
            _exitRequested = false;
        }
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_allowClose)
            return;
        eventArgs.Cancel = true;
        if (_closing || _checkingExit)
            return;
        if (!_exitRequested && _uiPreferences.CloseToTray)
        {
            HideForCloseShortcut();
            return;
        }
        _checkingExit = true;
        try
        {
            string exitMessage = Localization.Text("Exit.ConfirmMessage");
            if (_uiPreferences.ShowHints)
                exitMessage += "\n\n" + Localization.Text("Exit.ConfirmHint");
            if (_uiPreferences.ConfirmExit && !_confirmSettingsAction(
                exitMessage, Localization.Text("Exit.ConfirmTitle")))
                return;
            if (!CloseSectionWindows())
                return;
        }
        finally
        {
            _checkingExit = false;
        }
        _closing = true;
        try { _globalHotkeys?.Dispose(); }
        catch (HotkeyRegistrationException exception) { _shutdownOperationWarning = DescribeHotkeyError(exception.Error); }
        _globalHotkeys = null;
        _powerProtectionTimer.Stop();
        _powerProtectionTimer.Tick -= OnPowerProtectionTick;
        _tray?.Dispose();
        _tray = null;
        Interlocked.Increment(ref _connectionGeneration);
        _controller.StateChanged -= OnStateChanged;
        _controller.ConnectionLost -= OnConnectionLost;
        Localization.LanguageChanged -= OnLocalizationChanged;
        UpdateAvailability();
        SetStatus(Localization.Text("Shutdown.Pending"));
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
        List<string> errors = [];
        try
        {
            _lifetimeCancellation.Cancel();
        }
        catch (Exception exception)
        {
            errors.Add(DescribeError(exception));
        }
        try
        {
            await Task.WhenAll(_powerProtectionTask, _operationTask);
        }
        catch (Exception exception)
        {
            errors.Add(DescribeError(exception));
        }
        try
        {
            await PowerOffForExitAsync();
        }
        catch (Exception exception)
        {
            errors.Add(DescribeError(exception));
        }
        try
        {
            await _controller.DisconnectAsync();
        }
        catch (Exception exception)
        {
            errors.Add(DescribeError(exception));
        }
        try
        {
            await _controller.DisposeAsync();
        }
        catch (Exception exception)
        {
            errors.Add(DescribeError(exception));
        }
        _lifetimeCancellation.Dispose();
        if (_shutdownOperationWarning is not null)
            errors.Add(_shutdownOperationWarning);
        if (errors.Count != 0)
        {
            _showShutdownWarning(Localization.Text("Shutdown.Warning") + string.Join("\n", errors.Distinct()));
        }
        _allowClose = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Close));
    }

    internal static string DescribeError(Exception exception, bool useControllerMessage = true) => exception switch
    {
        SoundProfileException profileException => Localization.Text(profileException.Failure switch
        {
            SoundProfileFailure.NotConnected => "Profile.Error.NotConnected",
            SoundProfileFailure.StaleState => "Profile.Error.StaleState",
            SoundProfileFailure.InvalidState => "Profile.Error.InvalidState",
            SoundProfileFailure.Standby => "Profile.Error.Standby",
            _ => "Profile.Error.UnexpectedState"
        }),
        AggregateException aggregate => string.Join(" ", aggregate.Flatten().InnerExceptions.Select(error => DescribeError(error, useControllerMessage)).Distinct()),
        TimeoutException => Localization.Text("Error.Timeout"),
        UnauthorizedAccessException => Localization.Text("Error.AccessDenied"),
        COMException comException => Localization.Text("Error.Com", unchecked((uint)comException.HResult)),
        OperationCanceledException => Localization.Text("Error.Cancelled"),
        ObjectDisposedException => Localization.Text("Error.Disposed"),
        LocalizedOperationException localized when useControllerMessage => localized.Message,
        InvalidOperationException => Localization.Text("Error.InvalidOperation"),
        ArgumentException => Localization.Text("Error.Argument"),
        Win32Exception win32Exception => Localization.Text("Error.Windows", win32Exception.NativeErrorCode),
        _ => Localization.Text("Error.Unexpected", unchecked((uint)exception.HResult))
    };

    private static string GetCommandDescription(SoundbarCommand command) => command switch
    {
        SoundbarCommand.PowerOn => Localization.Text("Command.PowerOn"),
        SoundbarCommand.PowerOff => Localization.Text("Command.PowerOff"),
        SoundbarCommand.InputTv => Localization.Text("Command.InputTv"),
        SoundbarCommand.InputOptical => Localization.Text("Command.InputOptical"),
        SoundbarCommand.InputBluetooth => Localization.Text("Command.InputBluetooth"),
        SoundbarCommand.InputAnalog => Localization.Text("Command.InputAnalog"),
        SoundbarCommand.MuteOn => Localization.Text("Command.MuteOn"),
        SoundbarCommand.MuteOff => Localization.Text("Command.MuteOff"),
        SoundbarCommand.SubwooferUp => Localization.Text("Command.SubwooferUp"),
        SoundbarCommand.SubwooferDown => Localization.Text("Command.SubwooferDown"),
        SoundbarCommand.Stereo => Localization.Text("Command.Stereo"),
        SoundbarCommand.Standard => Localization.Text("Command.Standard"),
        SoundbarCommand.Movie3D => Localization.Text("Command.Movie3D"),
        SoundbarCommand.Game => Localization.Text("Command.Game"),
        SoundbarCommand.ClearVoiceOn => Localization.Text("Command.ClearVoiceOn"),
        SoundbarCommand.ClearVoiceOff => Localization.Text("Command.ClearVoiceOff"),
        SoundbarCommand.BassExtensionOn => Localization.Text("Command.BassExtensionOn"),
        SoundbarCommand.BassExtensionOff => Localization.Text("Command.BassExtensionOff"),
        SoundbarCommand.IndicatorsBright => Localization.Text("Command.IndicatorsBright"),
        SoundbarCommand.IndicatorsDim => Localization.Text("Command.IndicatorsDim"),
        SoundbarCommand.IndicatorsOff => Localization.Text("Command.IndicatorsOff"),
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    private static string FormatInput(SoundbarInput input) => input switch
    {
        SoundbarInput.Tv => Localization.Text("Input.Tv"),
        SoundbarInput.Optical => Localization.Text("Input.Optical"),
        SoundbarInput.Bluetooth => Localization.Text("Input.Bluetooth"),
        SoundbarInput.Analog => Localization.Text("Input.Analog"),
        _ => Localization.Text("Input.Unknown")
    };

    private static string FormatMode(SoundMode mode) => mode switch
    {
        SoundMode.Stereo => Localization.Text("Mode.Stereo"),
        SoundMode.Standard => Localization.Text("Mode.Standard"),
        SoundMode.Movie3D => Localization.Text("Mode.Movie3D"),
        SoundMode.Game => Localization.Text("Mode.Game"),
        _ => Localization.Text("Mode.Unknown")
    };

    private static string FormatBrightness(IndicatorBrightness brightness) => brightness switch
    {
        IndicatorBrightness.Bright => Localization.Text("Brightness.Bright"),
        IndicatorBrightness.Dim => Localization.Text("Brightness.Dim"),
        IndicatorBrightness.Off => Localization.Text("Brightness.Off"),
        _ => Localization.Text("Brightness.Unknown")
    };

    private static string FormatSwitch(bool enabled) => enabled ? Localization.Text("State.On") : Localization.Text("Brightness.Off");
    private static string FormatLevel(int value, int minimum, int maximum) => value >= minimum && value <= maximum
        ? value.ToString(DisplayCulture) : Localization.Text("Readout.UnknownLevel", value);
    private static string FormatTime(DateTimeOffset timestamp) => timestamp.ToLocalTime().ToString(Localization.Text("Readout.TimeFormat"), DisplayCulture);
    private static string KnownText(string value) => string.IsNullOrWhiteSpace(value) ? Localization.Text("Readout.Unknown") : value;
    private static string GetDeviceName(DiscoveredSoundbar? device) => device is null || string.IsNullOrWhiteSpace(device.Name) ? Localization.Text("Device.Unnamed") : device.Name;
    private static string FormatAddress(ulong address) => string.Join(":", Enumerable.Range(0, 6).Select(index => ((address >> ((5 - index) * 8)) & 0xFF).ToString("X2", CultureInfo.InvariantCulture)));

    private sealed class DeviceEntry(DiscoveredSoundbar device) : INotifyPropertyChanged
    {
        public DiscoveredSoundbar Device { get; } = device;
        public event PropertyChangedEventHandler? PropertyChanged;
        public string DisplayName => Localization.Text("Device.DiscoveredEntry", GetDeviceName(Device), FormatAddress(Device.Address), Device.SignalStrength);
        public void RefreshDisplayName() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        public override string ToString() => DisplayName;
    }
}
