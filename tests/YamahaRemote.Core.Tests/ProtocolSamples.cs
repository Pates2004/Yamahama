using YamahaRemote.Core;
using Xunit;

namespace YamahaRemote.Core.Tests;

internal static class ProtocolSamples
{
    public const string IdentityPayload = "04AABBCC27020109";
    public const string IdentityFrame = "CCAA0804AABBCC2702010990";
    public const string StatusPayload = "05000107001910000000000A0001";
    public const string StatusFrame = "CCAA0E05000107001910000000000A0001B1";
    public const string PowerOffFrame = "CCAA0340787FC6";
    public const string TvFrame = "CCAA034078DF66";

    public static byte[] StatusWith(int offset, byte value)
    {
        byte[] payload = Convert.FromHexString(StatusPayload);
        payload[offset] = value;
        return payload;
    }

    public static SoundbarState ParseState(byte[] payload)
    {
        Assert.True(SoundbarProtocol.TryParseState(payload, out SoundbarState? state));
        return Assert.IsType<SoundbarState>(state);
    }
}
