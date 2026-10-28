using System.Windows;
using System.Windows.Threading;
using YamahaRemote.Core;

namespace YamahaRemote.Windows;

public partial class MainWindow
{
    private readonly DispatcherTimer _powerProtectionTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromSeconds(5)
    };
    private readonly PowerProtectionPolicy _powerProtectionPolicy;
    private Task _powerProtectionTask = Task.CompletedTask;
    private CancellationTokenSource? _powerProtectionCancellation;
    private bool _powerProtectionMutationInProgress;
    private bool _powerProtectionUncertain;

    private void OnKeepPoweredOnChanged(object sender, RoutedEventArgs eventArgs)
    {
        ChangeSettings(preferences => preferences with { KeepPoweredOn = KeepPoweredOnCheckBox.IsChecked == true });
    }

    private void UpdatePowerProtectionMonitoring()
    {
        if (!_initialized || _closing || !_uiPreferences.KeepPoweredOn || !Connected)
        {
            _powerProtectionTimer.Stop();
            _powerProtectionPolicy.Reset();
            _powerProtectionCancellation?.Cancel();
            return;
        }
        if (_stateIsCurrent && _lastState is not null)
            _powerProtectionPolicy.Observe(_lastState);
        _powerProtectionTimer.Start();
    }

    private async void OnPowerProtectionTick(object? sender, EventArgs eventArgs) => await CheckPowerProtectionAsync();

    internal Task CheckPowerProtectionAsync()
    {
        if (_closing || _busy || !Connected || !_uiPreferences.KeepPoweredOn || !_powerProtectionTask.IsCompleted
            || !_powerProtectionPolicy.ShouldCheck(_lastState, _stateIsCurrent))
            return Task.CompletedTask;
        _powerProtectionTask = MonitorPowerAsync();
        return _powerProtectionTask;
    }

    private async Task MonitorPowerAsync()
    {
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _powerProtectionCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        long generation = Interlocked.Read(ref _connectionGeneration);
        bool attemptedPowerOn = false;
        bool SessionIsCurrent() => !_closing && Connected && generation == Interlocked.Read(ref _connectionGeneration);
        try
        {
            _powerProtectionPolicy.BeginCheck();
            SoundbarState? before = _controller.State;
            await _controller.RefreshAsync(token);
            token.ThrowIfCancellationRequested();
            if (!SessionIsCurrent())
                return;
            CaptureFreshState(before);
            if (!_busy && _uiPreferences.KeepPoweredOn && _stateIsCurrent && _lastState is not null
                && _powerProtectionPolicy.ShouldRestore(_lastState))
            {
                _powerProtectionPolicy.BeginRestore();
                attemptedPowerOn = true;
                _powerProtectionMutationInProgress = true;
                before = _controller.State;
                await _controller.SendAsync(SoundbarCommand.PowerOn, token);
                token.ThrowIfCancellationRequested();
                if (!SessionIsCurrent())
                    return;
                CaptureFreshState(before);
                if (_lastState?.Power != true)
                    throw new LocalizedOperationException("PowerProtection.NotConfirmed");
                _powerProtectionUncertain = false;
                SetStatus(Localization.Text("PowerProtection.Restored"));
            }
            _powerProtectionPolicy.Succeed(_lastState?.Power == true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (attemptedPowerOn)
            {
                _powerProtectionUncertain = true;
                string warning = Localization.Text("PowerProtection.Cancelled") + UncertainEffect;
                if (_closing)
                    _shutdownOperationWarning = warning;
                else if (generation == Interlocked.Read(ref _connectionGeneration))
                {
                    InvalidateReadout();
                    SetStatus(warning, important: true);
                }
            }
        }
        catch (Exception exception)
        {
            if (attemptedPowerOn)
            {
                _powerProtectionUncertain = true;
                if (_closing)
                    _shutdownOperationWarning = DescribeError(exception) + UncertainEffect;
            }
            if (SessionIsCurrent())
            {
                InvalidateReadout();
                if (_powerProtectionPolicy.Fail(attemptedPowerOn))
                    SetStatus(Localization.Text("PowerProtection.Failed") + DescribeError(exception)
                        + (attemptedPowerOn ? UncertainEffect : string.Empty), important: true);
            }
        }
        finally
        {
            _powerProtectionMutationInProgress = false;
            _powerProtectionCancellation = null;
            if (!_closing)
                UpdateAvailability();
        }
    }
}
