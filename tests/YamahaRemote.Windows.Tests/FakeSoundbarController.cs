using YamahaRemote.Core;

namespace YamahaRemote.Windows.Tests;

internal sealed class FakeSoundbarController : ISoundbarController
{
    public bool IsConnected { get; private set; }
    public SoundbarState? State { get; private set; }
    public event Action<SoundbarState>? StateChanged;
    public event Action<string>? ConnectionLost;
    public List<SoundbarCommand> Commands { get; } = [];
    public List<string> Operations { get; } = [];
    public List<CancellationToken> RefreshTokens { get; } = [];
    public List<CancellationToken> CommandTokens { get; } = [];
    public Action? BeforeRefresh { get; set; }
    public Action<SoundbarCommand>? AfterCommand { get; set; }
    public int VolumeRequests { get; private set; }
    public bool FailNextMutation { get; set; }
    public bool DelayMutation { get; set; }
    public TaskCompletionSource? MutationGate { get; set; }
    public bool FailConnection { get; set; }
    public int RefreshRequests { get; private set; }
    public int CompletedMutations { get; private set; }
    public TaskCompletionSource? RefreshGate { get; set; }
    public bool FailNextRefresh { get; set; }
    public bool? PowerOnNextRefresh { get; set; }
    public bool RefreshWithoutNewState { get; set; }
    public bool IgnorePowerOn { get; set; }
    public bool IgnorePowerOff { get; set; }
    public bool DelayDiscovery { get; set; }
    public bool DiscoveryCancelled { get; private set; }
    public bool Disposed { get; private set; }
    public byte ModelCode { get; set; } = 0x27;
    public List<ulong> ConnectionAddresses { get; } = [];

    public async Task<IReadOnlyList<DiscoveredSoundbar>> DiscoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (DelayDiscovery)
                await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            DiscoveryCancelled = true;
            throw;
        }
        return [new("Test soundbar (not hardware)", 1, -40)];
    }

    public Task<SoundbarIdentity> ConnectAsync(DiscoveredSoundbar device, CancellationToken cancellationToken)
    {
        Operations.Add("Connect");
        ConnectionAddresses.Add(device.Address);
        if (FailConnection)
            throw new InvalidOperationException("Test connection failure.");
        IsConnected = true;
        Update(new(InitiallyPoweredOn, SoundbarInput.Tv, 20, false, 0, SoundMode.Standard, false, false,
            IndicatorBrightness.Bright, DateTimeOffset.UtcNow));
        return Task.FromResult(new SoundbarIdentity("SR-C30A", "1.23", ModelCode, 0));
    }

    public Task DisconnectAsync()
    {
        Operations.Add("Disconnect");
        IsConnected = false;
        State = null;
        return Task.CompletedTask;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        Operations.Add("Refresh");
        RefreshTokens.Add(cancellationToken);
        RefreshRequests++;
        if (RefreshGate is not null)
            await RefreshGate.Task.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        BeforeRefresh?.Invoke();
        if (FailNextRefresh)
        {
            FailNextRefresh = false;
            throw new InvalidOperationException("Test refresh failure.");
        }
        if (RefreshWithoutNewState)
            return;
        SoundbarState next = PowerOnNextRefresh is bool power ? State! with { Power = power } : State!;
        PowerOnNextRefresh = null;
        Update(next);
    }

    public async Task SetVolumeAsync(int volume, CancellationToken cancellationToken)
    {
        VolumeRequests++;
        await BeforeMutationAsync(cancellationToken);
        Update(State! with { Volume = volume });
    }

    public async Task SendAsync(SoundbarCommand command, CancellationToken cancellationToken)
    {
        Operations.Add(command.ToString());
        CommandTokens.Add(cancellationToken);
        Commands.Add(command);
        await BeforeMutationAsync(cancellationToken);
        CompletedMutations++;
        SoundbarState current = State!;
        Update(command switch
        {
            SoundbarCommand.PowerOn => current with { Power = !IgnorePowerOn },
            SoundbarCommand.PowerOff => current with { Power = IgnorePowerOff && current.Power },
            SoundbarCommand.InputTv => current with { Input = SoundbarInput.Tv },
            SoundbarCommand.InputOptical => current with { Input = SoundbarInput.Optical },
            SoundbarCommand.InputBluetooth => current with { Input = SoundbarInput.Bluetooth },
            SoundbarCommand.InputAnalog => current with { Input = SoundbarInput.Analog },
            SoundbarCommand.MuteOn => current with { Muted = true },
            SoundbarCommand.MuteOff => current with { Muted = false },
            SoundbarCommand.SubwooferUp => current with { Subwoofer = Math.Min(4, current.Subwoofer + 1) },
            SoundbarCommand.SubwooferDown => current with { Subwoofer = Math.Max(-4, current.Subwoofer - 1) },
            SoundbarCommand.Stereo => current with { Mode = SoundMode.Stereo },
            SoundbarCommand.Standard => current with { Mode = SoundMode.Standard },
            SoundbarCommand.Movie3D => current with { Mode = SoundMode.Movie3D },
            SoundbarCommand.Game => current with { Mode = SoundMode.Game },
            SoundbarCommand.ClearVoiceOn => current with { ClearVoice = true },
            SoundbarCommand.ClearVoiceOff => current with { ClearVoice = false },
            SoundbarCommand.BassExtensionOn => current with { BassExtension = true },
            SoundbarCommand.BassExtensionOff => current with { BassExtension = false },
            SoundbarCommand.IndicatorsBright => current with { Brightness = IndicatorBrightness.Bright },
            SoundbarCommand.IndicatorsDim => current with { Brightness = IndicatorBrightness.Dim },
            SoundbarCommand.IndicatorsOff => current with { Brightness = IndicatorBrightness.Off },
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        });
        AfterCommand?.Invoke(command);
    }

    public bool InitiallyPoweredOn { get; set; } = true;
    public bool PublishStateBeforeMutationFailure { get; set; }

    public void PublishState(SoundbarState state) => Update(state);

    public void DropConnection()
    {
        IsConnected = false;
        State = null;
        ConnectionLost?.Invoke("Test connection loss, not hardware.");
    }

    private async Task BeforeMutationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (MutationGate is not null)
            await MutationGate.Task.WaitAsync(token);
        if (DelayMutation)
            await Task.Delay(Timeout.Infinite, token);
        token.ThrowIfCancellationRequested();
        if (FailNextMutation)
        {
            FailNextMutation = false;
            if (PublishStateBeforeMutationFailure)
                Update(State!);
            throw new InvalidOperationException("Test: brak potwierdzenia polecenia.");
        }
    }

    private void Update(SoundbarState next)
    {
        DateTimeOffset minimum = State?.ReceivedAt.AddMilliseconds(1) ?? DateTimeOffset.MinValue;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        State = next with { ReceivedAt = now > minimum ? now : minimum };
        StateChanged?.Invoke(State);
    }

    public ValueTask DisposeAsync()
    {
        Operations.Add("Dispose");
        Disposed = true;
        IsConnected = false;
        State = null;
        return ValueTask.CompletedTask;
    }
}
