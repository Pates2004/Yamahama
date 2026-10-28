using YamahaRemote.Core;

namespace YamahaRemote.Windows;

internal sealed class PowerProtectionPolicy(TimeProvider clock)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(60);
    private bool _armed;
    private bool _failureReported;
    private bool _restoreFailureReported;
    private bool _manualStandby;
    private bool _manualPowerOffPending;
    private DateTimeOffset _manualPowerOffThrough;
    private long? _lastCheck;
    private long? _lastRestore;
    private long? _lastFailure;

    public void Observe(SoundbarState state)
    {
        if (_manualStandby)
        {
            if (state.ReceivedAt <= _manualPowerOffThrough)
                return;
            if (_manualPowerOffPending || !state.Power)
            {
                _manualPowerOffThrough = state.ReceivedAt;
                return;
            }
            _manualStandby = false;
        }
        if (state.Power)
        {
            _armed = true;
            Succeed();
        }
    }

    public void BeginManualPowerOff(SoundbarState? state)
    {
        _armed = false;
        _manualStandby = true;
        _manualPowerOffPending = true;
        _manualPowerOffThrough = state?.ReceivedAt ?? DateTimeOffset.MinValue;
        Succeed();
    }

    public void CompleteManualPowerOff(SoundbarState? state)
    {
        if (!_manualPowerOffPending)
            return;
        if (state is not null && state.ReceivedAt > _manualPowerOffThrough)
            _manualPowerOffThrough = state.ReceivedAt;
        _manualPowerOffPending = false;
    }

    public bool ShouldCheck(SoundbarState? state, bool current) =>
        !_manualStandby && Elapsed(_lastFailure, RetryInterval) &&
        (Elapsed(_lastCheck, PollInterval) || current && state is not null && ShouldRestore(state));

    public bool ShouldRestore(SoundbarState state) =>
        !_manualStandby && _armed && !state.Power && Elapsed(_lastRestore, RetryInterval);

    public void BeginCheck() => _lastCheck = clock.GetTimestamp();

    public void BeginRestore() => _lastRestore = clock.GetTimestamp();

    public bool Fail(bool restoreAttempted = false)
    {
        _lastFailure = clock.GetTimestamp();
        _restoreFailureReported |= restoreAttempted;
        bool report = !_failureReported;
        _failureReported = true;
        return report;
    }

    public void Succeed(bool poweredOn = true)
    {
        _lastFailure = null;
        if (_restoreFailureReported && !poweredOn)
            return;
        _restoreFailureReported = false;
        _failureReported = false;
    }

    public void Reset()
    {
        _armed = false;
        _failureReported = false;
        _restoreFailureReported = false;
        _manualStandby = false;
        _manualPowerOffPending = false;
        _manualPowerOffThrough = DateTimeOffset.MinValue;
        _lastCheck = null;
        _lastRestore = null;
        _lastFailure = null;
    }

    private bool Elapsed(long? timestamp, TimeSpan interval) =>
        timestamp is not long start || clock.GetElapsedTime(start) >= interval;
}
