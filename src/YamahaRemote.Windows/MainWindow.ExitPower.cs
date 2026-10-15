using YamahaRemote.Core;

namespace YamahaRemote.Windows;

public partial class MainWindow
{
    internal async Task PowerOffForExitAsync(TimeSpan? timeout = null)
    {
        if (!_closing || !_uiPreferences.PowerOffOnExit || !Connected)
            return;

        using CancellationTokenSource cancellation = new(timeout ?? TimeSpan.FromSeconds(15));
        CancellationToken token = cancellation.Token;
        long generation = Interlocked.Read(ref _connectionGeneration);
        ulong? address = _lastDevice?.Address;
        bool attemptedPowerOff = false;

        void EnsureCurrentConnection()
        {
            token.ThrowIfCancellationRequested();
            if (!Connected || _identity?.ModelCode != 0x27 || address is null
                || _lastDevice?.Address != address || generation != Interlocked.Read(ref _connectionGeneration))
                throw new LocalizedOperationException("ExitPower.ConnectionChanged");
        }

        SoundbarState RequireFreshState(SoundbarState? previous)
        {
            EnsureCurrentConnection();
            SoundbarState? current = _controller.State;
            if (current is null || previous is not null && current.ReceivedAt <= previous.ReceivedAt)
                throw new LocalizedOperationException("Readout.FreshStateRequired");
            return current;
        }

        try
        {
            EnsureCurrentConnection();
            SoundbarState? before = _controller.State;
            await _controller.RefreshAsync(token);
            SoundbarState current = RequireFreshState(before);
            if (!current.Power)
                return;

            attemptedPowerOff = true;
            await _controller.SendAsync(SoundbarCommand.PowerOff, token);
            EnsureCurrentConnection();
            before = _controller.State;
            await _controller.RefreshAsync(token);
            if (RequireFreshState(before).Power)
                throw new LocalizedOperationException("ExitPower.NotConfirmed");
        }
        catch (Exception exception)
        {
            string reason = exception is OperationCanceledException && cancellation.IsCancellationRequested
                ? Localization.Text("Error.Timeout") : DescribeError(exception);
            throw new LocalizedOperationException(attemptedPowerOff ? "ExitPower.Uncertain" : "ExitPower.Failed", reason);
        }
    }
}
