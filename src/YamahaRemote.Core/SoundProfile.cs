namespace YamahaRemote.Core;

public sealed record SoundProfile(SoundMode Mode, bool ClearVoice, bool BassExtension, int Subwoofer)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || Mode == SoundMode.Unknown)
            throw new ArgumentException("The sound mode is not supported.", nameof(Mode));
        if (Subwoofer is < -4 or > 4)
            throw new ArgumentException("The subwoofer level must be between -4 and 4.", nameof(Subwoofer));
    }
}

public enum SoundProfileFailure { NotConnected, StaleState, InvalidState, Standby, UnexpectedState }

public sealed class SoundProfileException : InvalidOperationException
{
    public SoundProfileFailure Failure { get; }

    public SoundProfileException(SoundProfileFailure failure) : base(failure switch
    {
        SoundProfileFailure.NotConnected => "The soundbar is not connected.",
        SoundProfileFailure.StaleState => "The soundbar state is not fresh.",
        SoundProfileFailure.InvalidState => "The soundbar state is missing or unsupported.",
        SoundProfileFailure.Standby => "The soundbar must already be powered on to apply a sound profile.",
        SoundProfileFailure.UnexpectedState => "The soundbar state changed unexpectedly. The profile may be partially applied.",
        _ => "The sound profile operation failed."
    })
    {
        Failure = failure;
    }
}

public static class SoundProfileOperations
{
    public static async Task<SoundProfile> CaptureAsync(ISoundbarController controller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(controller);
        SoundbarState state = await RefreshStateAsync(controller, false, cancellationToken).ConfigureAwait(false);
        return new SoundProfile(state.Mode, state.ClearVoice, state.BassExtension, state.Subwoofer);
    }

    public static async Task ApplyAsync(ISoundbarController controller, SoundProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        SoundbarState state = await RefreshStateAsync(controller, true, cancellationToken).ConfigureAwait(false);

        if (state.Mode != profile.Mode)
        {
            SoundbarCommand command = profile.Mode switch
            {
                SoundMode.Stereo => SoundbarCommand.Stereo,
                SoundMode.Standard => SoundbarCommand.Standard,
                SoundMode.Movie3D => SoundbarCommand.Movie3D,
                SoundMode.Game => SoundbarCommand.Game,
                _ => throw new ArgumentException("The sound mode is not supported.", nameof(profile))
            };
            state = await SendAndConfirmAsync(controller, command, state, state with { Mode = profile.Mode }, cancellationToken)
                .ConfigureAwait(false);
        }

        if (state.ClearVoice != profile.ClearVoice)
        {
            SoundbarCommand command = profile.ClearVoice ? SoundbarCommand.ClearVoiceOn : SoundbarCommand.ClearVoiceOff;
            state = await SendAndConfirmAsync(controller, command, state, state with { ClearVoice = profile.ClearVoice }, cancellationToken)
                .ConfigureAwait(false);
        }

        if (state.BassExtension != profile.BassExtension)
        {
            SoundbarCommand command = profile.BassExtension ? SoundbarCommand.BassExtensionOn : SoundbarCommand.BassExtensionOff;
            state = await SendAndConfirmAsync(controller, command, state, state with { BassExtension = profile.BassExtension }, cancellationToken)
                .ConfigureAwait(false);
        }

        int subwooferSteps = Math.Abs(profile.Subwoofer - state.Subwoofer);
        int subwooferStep = Math.Sign(profile.Subwoofer - state.Subwoofer);
        SoundbarCommand subwooferCommand = subwooferStep > 0 ? SoundbarCommand.SubwooferUp : SoundbarCommand.SubwooferDown;
        for (int stepIndex = 0; stepIndex < subwooferSteps; stepIndex++)
        {
            state = await SendAndConfirmAsync(controller, subwooferCommand, state,
                state with { Subwoofer = state.Subwoofer + subwooferStep }, cancellationToken).ConfigureAwait(false);
        }

        await RefreshStateAsync(controller, true, cancellationToken, state).ConfigureAwait(false);
    }

    private static async Task<SoundbarState> RefreshStateAsync(
        ISoundbarController controller, bool requirePower, CancellationToken cancellationToken, SoundbarState? expected = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureConnected(controller);
        SoundbarState? previous = controller.State;
        if (expected is not null)
            EnsureExpectedState(ValidateState(previous, requirePower), expected);

        await controller.RefreshAsync(cancellationToken).ConfigureAwait(false);
        SoundbarState current = ReadState(controller, requirePower, cancellationToken);
        EnsureFreshState(current, previous?.ReceivedAt ?? DateTimeOffset.MinValue);
        if (expected is not null)
            EnsureExpectedState(current, expected);
        return current;
    }

    private static async Task<SoundbarState> SendAndConfirmAsync(ISoundbarController controller, SoundbarCommand command,
        SoundbarState expected, SoundbarState requested, CancellationToken cancellationToken)
    {
        SoundbarState before = ReadState(controller, true, cancellationToken);
        EnsureExpectedState(before, expected);
        await controller.SendAsync(command, cancellationToken).ConfigureAwait(false);
        SoundbarState after = ReadState(controller, true, cancellationToken);
        EnsureFreshState(after, before.ReceivedAt);
        EnsureExpectedState(after, requested);
        return after;
    }

    private static SoundbarState ReadState(ISoundbarController controller, bool requirePower, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureConnected(controller);
        return ValidateState(controller.State, requirePower);
    }

    private static void EnsureConnected(ISoundbarController controller)
    {
        if (!controller.IsConnected)
            throw new SoundProfileException(SoundProfileFailure.NotConnected);
    }

    private static SoundbarState ValidateState(SoundbarState? state, bool requirePower)
    {
        if (state is null || !Enum.IsDefined(state.Input) || state.Volume is < 0 or > 50 ||
            state.Subwoofer is < -4 or > 4 || !Enum.IsDefined(state.Mode) || state.Mode == SoundMode.Unknown ||
            !Enum.IsDefined(state.Brightness))
            throw new SoundProfileException(SoundProfileFailure.InvalidState);
        if (requirePower && !state.Power)
            throw new SoundProfileException(SoundProfileFailure.Standby);
        return state;
    }

    private static void EnsureFreshState(SoundbarState state, DateTimeOffset previousTimestamp)
    {
        if (state.ReceivedAt <= previousTimestamp)
            throw new SoundProfileException(SoundProfileFailure.StaleState);
    }

    private static void EnsureExpectedState(SoundbarState current, SoundbarState expected)
    {
        if (current.ReceivedAt < expected.ReceivedAt)
            throw new SoundProfileException(SoundProfileFailure.StaleState);
        if (current with { ReceivedAt = expected.ReceivedAt } != expected)
            throw new SoundProfileException(SoundProfileFailure.UnexpectedState);
    }
}
