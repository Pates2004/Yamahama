namespace YamahaRemote.Core;

public static class SoundbarProtocol
{
    public static readonly Guid ServiceId = new("945ca2b0-852c-4ab8-b654-354df41c2795");
    public static readonly Guid WriteId = new("0c50e7fa-594c-408b-ae0d-b53b884b7c08");
    public static readonly Guid NotifyId = new("5cafe9de-e7b0-4e0b-8fb9-2da91a7ae3ed");
    public static byte[] Handshake => [0x01, 0x48, 0x54, 0x53, 0x20, 0x43, 0x6f, 0x6e, 0x74];
    public static byte[] StatusRequest => [0x03, 0x05];

    public static byte[] Encode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is < 1 or > 124)
            throw new ArgumentOutOfRangeException(nameof(payload));
        byte[] frame = new byte[payload.Length + 4];
        frame[0] = 0xcc;
        frame[1] = 0xaa;
        frame[2] = (byte)payload.Length;
        payload.CopyTo(frame.AsSpan(3));
        int checksum = payload.Length;
        foreach (byte value in payload)
            checksum += value;
        frame[^1] = unchecked((byte)-checksum);
        return frame;
    }

    public static byte[] VolumePayload(int volume)
    {
        if (volume is < 0 or > 50)
            throw new ArgumentOutOfRangeException(nameof(volume));
        return [0x41, (byte)volume];
    }

    public static byte[] CommandPayload(SoundbarCommand command) => command switch
    {
        SoundbarCommand.PowerOn => [0x40, 0x78, 0x7e],
        SoundbarCommand.PowerOff => [0x40, 0x78, 0x7f],
        SoundbarCommand.InputTv => [0x40, 0x78, 0xdf],
        SoundbarCommand.InputOptical => [0x40, 0x78, 0x49],
        SoundbarCommand.InputBluetooth => [0x40, 0x78, 0x29],
        SoundbarCommand.InputAnalog => [0x40, 0x78, 0xd1],
        SoundbarCommand.MuteOn => [0x40, 0x7e, 0xa2],
        SoundbarCommand.MuteOff => [0x40, 0x7e, 0xa3],
        SoundbarCommand.SubwooferUp => [0x40, 0x78, 0x4c],
        SoundbarCommand.SubwooferDown => [0x40, 0x78, 0x4d],
        SoundbarCommand.Stereo => [0x40, 0x78, 0x50],
        SoundbarCommand.Standard => [0x40, 0x7e, 0xf1],
        SoundbarCommand.Movie3D => [0x40, 0x78, 0xd9],
        SoundbarCommand.Game => [0x40, 0x78, 0xdc],
        SoundbarCommand.ClearVoiceOn => [0x40, 0x7e, 0x80],
        SoundbarCommand.ClearVoiceOff => [0x40, 0x7e, 0x82],
        SoundbarCommand.BassExtensionOn => [0x40, 0x78, 0x6e],
        SoundbarCommand.BassExtensionOff => [0x40, 0x78, 0x6f],
        SoundbarCommand.IndicatorsBright => [0x51, 0x00],
        SoundbarCommand.IndicatorsDim => [0x51, 0x01],
        SoundbarCommand.IndicatorsOff => [0x51, 0x02],
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    public static bool TryParseIdentity(ReadOnlySpan<byte> payload, out SoundbarIdentity? identity)
    {
        identity = null;
        if (payload.Length < 8 || payload[0] != 0x04)
            return false;
        byte modelCode = payload[4];
        string model = modelCode switch
        {
            0x27 => "SR-C30A",
            0x28 => "ATS-C300",
            0x25 => "SR-C20A",
            _ => $"0x{modelCode:X2}"
        };
        identity = new(model, $"{payload[6]}.{payload[7]:D2}", modelCode, payload[5]);
        return true;
    }

    public static bool TryParseState(ReadOnlySpan<byte> payload, out SoundbarState? state)
    {
        state = null;
        if (payload.Length != 14 || payload[0] != 0x05)
            return false;
        ReadOnlySpan<byte> data = payload[1..];
        if (!Enum.IsDefined((SoundbarInput)data[2]) || data[4] > 50 ||
            data[5] > 32 || data[5] % 4 != 0 || data[12] > 2)
            return false;
        SoundMode mode = data[9] == 1 ? SoundMode.Stereo : data[9] == 0 ? data[10] switch
        {
            10 => SoundMode.Standard,
            3 => SoundMode.Movie3D,
            12 => SoundMode.Game,
            _ => SoundMode.Unknown
        } : SoundMode.Unknown;
        state = new(
            (data[1] & 1) != 0, (SoundbarInput)data[2], data[4], data[3] != 0,
            (data[5] - 16) / 4, mode, (data[11] & 0x04) != 0,
            (data[11] & 0x20) != 0, (IndicatorBrightness)data[12], DateTimeOffset.UtcNow);
        return true;
    }

    public static bool IsCommandConfirmed(SoundbarCommand command, SoundbarState before, SoundbarState after) => command switch
    {
        SoundbarCommand.PowerOn => after.Power,
        SoundbarCommand.PowerOff => !after.Power,
        SoundbarCommand.InputTv => after.Input == SoundbarInput.Tv,
        SoundbarCommand.InputOptical => after.Input == SoundbarInput.Optical,
        SoundbarCommand.InputBluetooth => after.Input == SoundbarInput.Bluetooth,
        SoundbarCommand.InputAnalog => after.Input == SoundbarInput.Analog,
        SoundbarCommand.MuteOn => after.Muted,
        SoundbarCommand.MuteOff => !after.Muted,
        SoundbarCommand.SubwooferUp => after.Subwoofer == Math.Min(before.Subwoofer + 1, 4),
        SoundbarCommand.SubwooferDown => after.Subwoofer == Math.Max(before.Subwoofer - 1, -4),
        SoundbarCommand.Stereo => after.Mode == SoundMode.Stereo,
        SoundbarCommand.Standard => after.Mode == SoundMode.Standard,
        SoundbarCommand.Movie3D => after.Mode == SoundMode.Movie3D,
        SoundbarCommand.Game => after.Mode == SoundMode.Game,
        SoundbarCommand.ClearVoiceOn => after.ClearVoice,
        SoundbarCommand.ClearVoiceOff => !after.ClearVoice,
        SoundbarCommand.BassExtensionOn => after.BassExtension,
        SoundbarCommand.BassExtensionOff => !after.BassExtension,
        SoundbarCommand.IndicatorsBright => after.Brightness == IndicatorBrightness.Bright,
        SoundbarCommand.IndicatorsDim => after.Brightness == IndicatorBrightness.Dim,
        SoundbarCommand.IndicatorsOff => after.Brightness == IndicatorBrightness.Off,
        _ => false
    };
}

public sealed class PacketDecoder
{
    private readonly List<byte> buffer = new(128);

    public IReadOnlyList<byte[]> Append(ReadOnlySpan<byte> fragment)
    {
        List<byte[]> packets = [];
        foreach (byte value in fragment)
        {
            buffer.Add(value);
            while (true)
            {
                while (buffer.Count >= 2 && (buffer[0] != 0xcc || buffer[1] != 0xaa))
                    buffer.RemoveAt(0);
                if (buffer.Count < 3)
                    break;
                int length = buffer[2];
                if (length is < 1 or > 124)
                {
                    buffer.RemoveAt(0);
                    continue;
                }
                if (buffer.Count < length + 4)
                    break;
                int checksum = 0;
                for (int index = 2; index < length + 4; index++)
                    checksum += buffer[index];
                if ((checksum & 0xff) == 0)
                {
                    packets.Add(buffer.GetRange(3, length).ToArray());
                    buffer.RemoveRange(0, length + 4);
                }
                else
                {
                    buffer.RemoveAt(0);
                }
            }
        }
        return packets;
    }

    public void Reset() => buffer.Clear();
}
