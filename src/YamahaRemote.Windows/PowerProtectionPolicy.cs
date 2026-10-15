using YamahaRemote.Core;

namespace YamahaRemote.Windows;

internal sealed class PowerProtectionPolicy(TimeProvider clock)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(60);
    private bool _armed;
    private bool _failureReported;
    private long? _lastCheck;
    private long? _lastRestore;
    private long? _lastFailure;

    public void Observe(SoundbarState state)
    {
        if (state.Power)
            _armed = true;
    }

    public bool ShouldCheck(SoundbarState? state, bool current) =>
        Elapsed(_lastFailure, RetryInterval) &&
        (Elapsed(_lastCheck, PollInterval) || current && state is not null && ShouldRestore(state));

    public bool ShouldRestore(SoundbarState state) =>
        _armed && !state.Power && Elapsed(_lastRestore, RetryInterval);

    public void BeginCheck() => _lastCheck = clock.GetTimestamp();

    public void BeginRestore() => _lastRestore = clock.GetTimestamp();

    public bool Fail()
    {
        _lastFailure = clock.GetTimestamp();
        bool report = !_failureReported;
        _failureReported = true;
        return report;
    }

    public void Succeed()
    {
        _lastFailure = null;
        _failureReported = false;
    }

    public void Reset()
    {
        _armed = false;
        _failureReported = false;
        _lastCheck = null;
        _lastRestore = null;
        _lastFailure = null;
    }

    private bool Elapsed(long? timestamp, TimeSpan interval) =>
        timestamp is not long start || clock.GetElapsedTime(start) >= interval;
}
