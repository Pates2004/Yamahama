namespace YamahaRemote.Core;

public enum SoundbarInput { Bluetooth = 5, Tv = 7, Optical = 10, Analog = 12 }
public enum SoundMode { Unknown, Stereo, Standard, Movie3D, Game }
public enum IndicatorBrightness { Bright, Dim, Off }

public enum SoundbarCommand
{
    PowerOn, PowerOff, InputTv, InputOptical, InputBluetooth, InputAnalog,
    MuteOn, MuteOff, SubwooferUp, SubwooferDown,
    Stereo, Standard, Movie3D, Game,
    ClearVoiceOn, ClearVoiceOff, BassExtensionOn, BassExtensionOff,
    IndicatorsBright, IndicatorsDim, IndicatorsOff
}

public sealed record DiscoveredSoundbar(string Name, ulong Address, short SignalStrength);
public sealed record SoundbarIdentity(string Model, string Firmware, byte ModelCode, byte Region);
public sealed record SoundbarState(
    bool Power, SoundbarInput Input, int Volume, bool Muted, int Subwoofer,
    SoundMode Mode, bool ClearVoice, bool BassExtension,
    IndicatorBrightness Brightness, DateTimeOffset ReceivedAt);

public interface ISoundbarController : IAsyncDisposable
{
    bool IsConnected { get; }
    SoundbarState? State { get; }
    event Action<SoundbarState>? StateChanged;
    event Action<string>? ConnectionLost;
    Task<IReadOnlyList<DiscoveredSoundbar>> DiscoverAsync(CancellationToken cancellationToken);
    Task<SoundbarIdentity> ConnectAsync(DiscoveredSoundbar device, CancellationToken cancellationToken);
    Task DisconnectAsync();
    Task SendAsync(SoundbarCommand command, CancellationToken cancellationToken);
    Task SetVolumeAsync(int volume, CancellationToken cancellationToken);
    Task RefreshAsync(CancellationToken cancellationToken);
}
