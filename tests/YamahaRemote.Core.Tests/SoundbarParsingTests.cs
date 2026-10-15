using YamahaRemote.Core;
using Xunit;

namespace YamahaRemote.Core.Tests;

public sealed class SoundbarParsingTests
{
    [Theory]
    [InlineData("04AABBCC27020109", "SR-C30A", 0x27, 2, "1.09")]
    [InlineData("0400000028000210", "ATS-C300", 0x28, 0, "2.16")]
    [InlineData("0401020325050000", "SR-C20A", 0x25, 5, "0.00")]
    [InlineData("04AABBCC0001010A", "0x00", 0, 1, "1.10")]
    [InlineData("04AABBCCABFF02FF", "0xAB", 0xAB, 255, "2.255")]
    [InlineData("04000000FF04FF01", "0xFF", 0xFF, 4, "255.01")]
    public void IdentityUsesDocumentedOffsetsAndPreservesUnknownModels(
        string payload, string model, int modelCode, int region, string firmware)
    {
        Assert.True(SoundbarProtocol.TryParseIdentity(Convert.FromHexString(payload), out SoundbarIdentity? identity));

        SoundbarIdentity actual = Assert.IsType<SoundbarIdentity>(identity);
        Assert.Equal(model, actual.Model);
        Assert.Equal(modelCode, actual.ModelCode);
        Assert.Equal(region, actual.Region);
        Assert.Equal(firmware, actual.Firmware);
    }

    [Fact]
    public void IdentityRejectsEveryTruncatedPrefixWithoutReturningAnObject()
    {
        byte[] payload = Convert.FromHexString(ProtocolSamples.IdentityPayload);

        for (int length = 0; length < payload.Length; length++)
        {
            Assert.False(SoundbarProtocol.TryParseIdentity(payload.AsSpan(0, length), out SoundbarIdentity? identity));
            Assert.Null(identity);
        }
    }

    [Fact]
    public void IdentityRejectsEveryOtherMessageType()
    {
        byte[] payload = Convert.FromHexString(ProtocolSamples.IdentityPayload);

        for (int messageType = 0; messageType <= 255; messageType++)
        {
            if (messageType == 0x04)
                continue;
            payload[0] = (byte)messageType;
            Assert.False(SoundbarProtocol.TryParseIdentity(payload, out SoundbarIdentity? identity));
            Assert.Null(identity);
        }
    }

    [Fact]
    public void FullStatusDecodesTheFourteenBytePayloadAndReceptionTime()
    {
        DateTimeOffset earliest = DateTimeOffset.UtcNow;
        SoundbarState state = ProtocolSamples.ParseState(Convert.FromHexString(ProtocolSamples.StatusPayload));
        DateTimeOffset latest = DateTimeOffset.UtcNow;

        Assert.True(state.Power);
        Assert.Equal(SoundbarInput.Tv, state.Input);
        Assert.Equal(25, state.Volume);
        Assert.False(state.Muted);
        Assert.Equal(0, state.Subwoofer);
        Assert.Equal(SoundMode.Standard, state.Mode);
        Assert.False(state.ClearVoice);
        Assert.False(state.BassExtension);
        Assert.Equal(IndicatorBrightness.Dim, state.Brightness);
        Assert.Equal(TimeSpan.Zero, state.ReceivedAt.Offset);
        Assert.InRange(state.ReceivedAt, earliest, latest);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(254, false)]
    [InlineData(255, true)]
    public void PowerReadsOnlyBitZero(int raw, bool expected)
    {
        Assert.Equal(expected, ProtocolSamples.ParseState(ProtocolSamples.StatusWith(2, (byte)raw)).Power);
    }

    [Theory]
    [InlineData(5, SoundbarInput.Bluetooth)]
    [InlineData(7, SoundbarInput.Tv)]
    [InlineData(10, SoundbarInput.Optical)]
    [InlineData(12, SoundbarInput.Analog)]
    public void InputMatchesTheDocumentedWireValue(int raw, SoundbarInput expected)
    {
        Assert.Equal(raw, (int)expected);
        Assert.Equal(expected, ProtocolSamples.ParseState(ProtocolSamples.StatusWith(3, (byte)raw)).Input);
    }

    [Fact]
    public void EveryUnknownInputIsRejected()
    {
        int[] knownInputs = [5, 7, 10, 12];

        foreach (int raw in Enumerable.Range(0, 256).Except(knownInputs))
            AssertInvalidState(ProtocolSamples.StatusWith(3, (byte)raw));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(255, true)]
    public void MuteIsIndependentOfVolume(int raw, bool expected)
    {
        foreach (byte volume in new byte[] { 0, 25, 50 })
        {
            byte[] payload = ProtocolSamples.StatusWith(4, (byte)raw);
            payload[5] = volume;
            SoundbarState state = ProtocolSamples.ParseState(payload);

            Assert.Equal(expected, state.Muted);
            Assert.Equal(volume, state.Volume);
        }
    }

    [Fact]
    public void VolumeAcceptsEveryValueFromZeroToFiftyAndRejectsAllOtherBytes()
    {
        for (int volume = 0; volume <= 255; volume++)
        {
            byte[] payload = ProtocolSamples.StatusWith(5, (byte)volume);
            if (volume <= 50)
                Assert.Equal(volume, ProtocolSamples.ParseState(payload).Volume);
            else
                AssertInvalidState(payload);
        }
    }

    [Theory]
    [InlineData(0, -4)]
    [InlineData(4, -3)]
    [InlineData(8, -2)]
    [InlineData(12, -1)]
    [InlineData(16, 0)]
    [InlineData(20, 1)]
    [InlineData(24, 2)]
    [InlineData(28, 3)]
    [InlineData(32, 4)]
    public void SubwooferMatchesEveryDocumentedStep(int raw, int expected)
    {
        Assert.Equal(expected, ProtocolSamples.ParseState(ProtocolSamples.StatusWith(6, (byte)raw)).Subwoofer);
    }

    [Fact]
    public void SubwooferRejectsOutOfRangeAndUnalignedValuesInsteadOfRounding()
    {
        int[] validSteps = [0, 4, 8, 12, 16, 20, 24, 28, 32];

        foreach (int raw in Enumerable.Range(0, 256).Except(validSteps))
            AssertInvalidState(ProtocolSamples.StatusWith(6, (byte)raw));
    }

    [Theory]
    [InlineData(0, 10, SoundMode.Standard)]
    [InlineData(0, 3, SoundMode.Movie3D)]
    [InlineData(0, 12, SoundMode.Game)]
    [InlineData(0, 0, SoundMode.Unknown)]
    [InlineData(0, 255, SoundMode.Unknown)]
    [InlineData(1, 10, SoundMode.Stereo)]
    [InlineData(1, 3, SoundMode.Stereo)]
    [InlineData(1, 12, SoundMode.Stereo)]
    [InlineData(1, 255, SoundMode.Stereo)]
    [InlineData(2, 10, SoundMode.Unknown)]
    [InlineData(255, 3, SoundMode.Unknown)]
    public void SoundModesRespectStereoPrecedenceAndUnknownValues(int stereo, int program, SoundMode expected)
    {
        byte[] payload = ProtocolSamples.StatusWith(10, (byte)stereo);
        payload[11] = (byte)program;

        Assert.Equal(expected, ProtocolSamples.ParseState(payload).Mode);
    }

    [Theory]
    [InlineData(0x00, false, false)]
    [InlineData(0x01, false, false)]
    [InlineData(0x02, false, false)]
    [InlineData(0x04, true, false)]
    [InlineData(0x08, false, false)]
    [InlineData(0x10, false, false)]
    [InlineData(0x20, false, true)]
    [InlineData(0x40, false, false)]
    [InlineData(0x80, false, false)]
    [InlineData(0x24, true, true)]
    [InlineData(0xDB, false, false)]
    [InlineData(0xDF, true, false)]
    [InlineData(0xFB, false, true)]
    [InlineData(0xFF, true, true)]
    public void ClearVoiceAndBassExtensionReadIndependentFlagBits(int flags, bool clearVoice, bool bassExtension)
    {
        SoundbarState state = ProtocolSamples.ParseState(ProtocolSamples.StatusWith(12, (byte)flags));

        Assert.Equal(clearVoice, state.ClearVoice);
        Assert.Equal(bassExtension, state.BassExtension);
        Assert.Equal(0, state.Subwoofer);
    }

    [Theory]
    [InlineData(0, IndicatorBrightness.Bright)]
    [InlineData(1, IndicatorBrightness.Dim)]
    [InlineData(2, IndicatorBrightness.Off)]
    public void BrightnessMatchesAllThreeValues(int raw, IndicatorBrightness expected)
    {
        Assert.Equal(expected, ProtocolSamples.ParseState(ProtocolSamples.StatusWith(13, (byte)raw)).Brightness);
    }

    [Fact]
    public void UnknownBrightnessIsRejected()
    {
        for (int raw = 3; raw <= 255; raw++)
            AssertInvalidState(ProtocolSamples.StatusWith(13, (byte)raw));
    }

    [Fact]
    public void ReservedAndToneFieldsDoNotShiftOrContaminateTheSupportedState()
    {
        byte[] payload = Convert.FromHexString(ProtocolSamples.StatusPayload);
        SoundbarState baseline = ProtocolSamples.ParseState(payload);
        payload[1] = 0xAA;
        payload[7] = 0xBB;
        payload[8] = 0xCC;
        payload[9] = 0xDD;
        SoundbarState actual = ProtocolSamples.ParseState(payload);

        Assert.Equal(baseline, actual with { ReceivedAt = baseline.ReceivedAt });
    }

    [Fact]
    public void StatusRequiresExactlyFourteenBytesAndTheCorrectMessageType()
    {
        byte[] payload = Convert.FromHexString(ProtocolSamples.StatusPayload);

        for (int length = 0; length < 14; length++)
            AssertInvalidState(payload[..length]);
        AssertInvalidState([.. payload, 0]);
        AssertInvalidState([.. payload, .. new byte[110]]);
        for (int messageType = 0; messageType <= 255; messageType++)
        {
            if (messageType != 0x05)
                AssertInvalidState(ProtocolSamples.StatusWith(0, (byte)messageType));
        }
    }

    [Fact]
    public void RequestsAndCommandEchoesNeverBecomeConfirmedState()
    {
        AssertInvalidState(SoundbarProtocol.Handshake);
        AssertInvalidState(SoundbarProtocol.StatusRequest);
        AssertInvalidState(Convert.FromHexString(ProtocolSamples.IdentityPayload));
        foreach (SoundbarCommand command in Enum.GetValues<SoundbarCommand>())
            AssertInvalidState(SoundbarProtocol.CommandPayload(command));
        for (int volume = 0; volume <= 50; volume++)
            AssertInvalidState(SoundbarProtocol.VolumePayload(volume));
    }

    [Fact]
    public void AnInvalidReplyDoesNotReuseThePreviousOutputObject()
    {
        Assert.True(SoundbarProtocol.TryParseState(Convert.FromHexString(ProtocolSamples.StatusPayload), out SoundbarState? state));
        Assert.False(SoundbarProtocol.TryParseState([], out state));
        Assert.Null(state);
        Assert.True(SoundbarProtocol.TryParseIdentity(Convert.FromHexString(ProtocolSamples.IdentityPayload), out SoundbarIdentity? identity));
        Assert.False(SoundbarProtocol.TryParseIdentity([], out identity));
        Assert.Null(identity);
    }

    private static void AssertInvalidState(byte[] payload)
    {
        Assert.False(SoundbarProtocol.TryParseState(payload, out SoundbarState? state));
        Assert.Null(state);
    }
}
