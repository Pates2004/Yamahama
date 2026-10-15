using YamahaRemote.Core;
using Xunit;

namespace YamahaRemote.Core.Tests;

public sealed class SoundbarProtocolTests
{
    public static TheoryData<SoundbarCommand, string, string> CommandVectors => new()
    {
        { SoundbarCommand.PowerOn, "40787E", "CCAA0340787EC7" },
        { SoundbarCommand.PowerOff, "40787F", "CCAA0340787FC6" },
        { SoundbarCommand.InputTv, "4078DF", "CCAA034078DF66" },
        { SoundbarCommand.InputOptical, "407849", "CCAA03407849FC" },
        { SoundbarCommand.InputBluetooth, "407829", "CCAA034078291C" },
        { SoundbarCommand.InputAnalog, "4078D1", "CCAA034078D174" },
        { SoundbarCommand.MuteOn, "407EA2", "CCAA03407EA29D" },
        { SoundbarCommand.MuteOff, "407EA3", "CCAA03407EA39C" },
        { SoundbarCommand.SubwooferUp, "40784C", "CCAA0340784CF9" },
        { SoundbarCommand.SubwooferDown, "40784D", "CCAA0340784DF8" },
        { SoundbarCommand.Stereo, "407850", "CCAA03407850F5" },
        { SoundbarCommand.Standard, "407EF1", "CCAA03407EF14E" },
        { SoundbarCommand.Movie3D, "4078D9", "CCAA034078D96C" },
        { SoundbarCommand.Game, "4078DC", "CCAA034078DC69" },
        { SoundbarCommand.ClearVoiceOn, "407E80", "CCAA03407E80BF" },
        { SoundbarCommand.ClearVoiceOff, "407E82", "CCAA03407E82BD" },
        { SoundbarCommand.BassExtensionOn, "40786E", "CCAA0340786ED7" },
        { SoundbarCommand.BassExtensionOff, "40786F", "CCAA0340786FD6" },
        { SoundbarCommand.IndicatorsBright, "5100", "CCAA025100AD" },
        { SoundbarCommand.IndicatorsDim, "5101", "CCAA025101AC" },
        { SoundbarCommand.IndicatorsOff, "5102", "CCAA025102AB" }
    };

    [Theory]
    [MemberData(nameof(CommandVectors))]
    public void CommandsMatchIndependentApkPayloadsAndFixedChecksumVectors(
        SoundbarCommand command, string expectedPayload, string expectedFrame)
    {
        byte[] payload = SoundbarProtocol.CommandPayload(command);

        Assert.Equal(expectedPayload, Convert.ToHexString(payload));
        Assert.Equal(expectedFrame, Convert.ToHexString(SoundbarProtocol.Encode(payload)));
        Assert.Equal(Convert.FromHexString(expectedPayload),
            Assert.Single(new PacketDecoder().Append(Convert.FromHexString(expectedFrame))));
    }

    [Fact]
    public void CommandVectorsCoverEveryCommandExactlyOnce()
    {
        SoundbarCommand[] covered = CommandVectors.Select(row => (SoundbarCommand)row[0]).ToArray();

        Assert.Equal(21, covered.Length);
        Assert.Equal(covered.Length, covered.Distinct().Count());
        Assert.Equal(Enum.GetValues<SoundbarCommand>().Order(), covered.Order());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    [InlineData(int.MaxValue)]
    public void UnknownCommandsAreRejected(int command)
    {
        Assert.Throws<ArgumentOutOfRangeException>("command",
            () => SoundbarProtocol.CommandPayload((SoundbarCommand)command));
    }

    [Theory]
    [InlineData("00", "CCAA0100FF")]
    [InlineData("FF", "CCAA01FF00")]
    [InlineData("0148545320436F6E74", "CCAA090148545320436F6E7453")]
    [InlineData("0305", "CCAA020305F6")]
    [InlineData("4100", "CCAA024100BD")]
    [InlineData("4119", "CCAA024119A4")]
    [InlineData("4132", "CCAA0241328B")]
    [InlineData(ProtocolSamples.IdentityPayload, ProtocolSamples.IdentityFrame)]
    [InlineData(ProtocolSamples.StatusPayload, ProtocolSamples.StatusFrame)]
    public void EncoderMatchesLiteralVectors(string payload, string expectedFrame)
    {
        Assert.Equal(expectedFrame,
            Convert.ToHexString(SoundbarProtocol.Encode(Convert.FromHexString(payload))));
    }

    [Fact]
    public void HandshakeAndPollMatchWireProtocol()
    {
        Assert.Equal("0148545320436F6E74", Convert.ToHexString(SoundbarProtocol.Handshake));
        Assert.Equal("HTS Cont", System.Text.Encoding.ASCII.GetString(SoundbarProtocol.Handshake[1..]));
        Assert.Equal("0305", Convert.ToHexString(SoundbarProtocol.StatusRequest));
        Assert.Equal(new Guid("945ca2b0-852c-4ab8-b654-354df41c2795"), SoundbarProtocol.ServiceId);
        Assert.Equal(new Guid("0c50e7fa-594c-408b-ae0d-b53b884b7c08"), SoundbarProtocol.WriteId);
        Assert.Equal(new Guid("5cafe9de-e7b0-4e0b-8fb9-2da91a7ae3ed"), SoundbarProtocol.NotifyId);
    }

    [Fact]
    public void ReturnedPayloadsCannotMutateLaterRequests()
    {
        SoundbarProtocol.Handshake[0] = 0;
        SoundbarProtocol.StatusRequest[0] = 0;
        SoundbarProtocol.CommandPayload(SoundbarCommand.PowerOff)[0] = 0;
        SoundbarProtocol.VolumePayload(25)[0] = 0;

        Assert.Equal("0148545320436F6E74", Convert.ToHexString(SoundbarProtocol.Handshake));
        Assert.Equal("0305", Convert.ToHexString(SoundbarProtocol.StatusRequest));
        Assert.Equal("40787F", Convert.ToHexString(SoundbarProtocol.CommandPayload(SoundbarCommand.PowerOff)));
        Assert.Equal("4119", Convert.ToHexString(SoundbarProtocol.VolumePayload(25)));
    }

    [Fact]
    public void EverySupportedVolumeHasAnAbsoluteVolumePayload()
    {
        for (int volume = 0; volume <= 50; volume++)
            Assert.Equal(new byte[] { 0x41, (byte)volume }, SoundbarProtocol.VolumePayload(volume));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(51)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(int.MaxValue)]
    public void VolumeOutsideTheSupportedRangeIsRejected(int volume)
    {
        Assert.Throws<ArgumentOutOfRangeException>("volume", () => SoundbarProtocol.VolumePayload(volume));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(125)]
    [InlineData(128)]
    [InlineData(256)]
    public void InvalidPayloadLengthsAreRejected(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>("payload", () => SoundbarProtocol.Encode(new byte[length]));
    }

    [Theory]
    [InlineData(0, 0x84)]
    [InlineData(0xFF, 0)]
    public void MaximumLengthUsesTheKnownChecksumWithoutOverflow(int value, int checksum)
    {
        byte[] payload = Enumerable.Repeat((byte)value, 124).ToArray();
        byte[] expected = [0xCC, 0xAA, 0x7C, .. payload, (byte)checksum];

        Assert.Equal(expected, SoundbarProtocol.Encode(payload));
        Assert.Equal(payload, Assert.Single(new PacketDecoder().Append(expected)));
    }

    [Fact]
    public void EncodingDoesNotModifyOrRetainTheInputBuffer()
    {
        byte[] payload = [0x40, 0x78, 0x7F];
        byte[] frame = SoundbarProtocol.Encode(payload);

        Assert.Equal("40787F", Convert.ToHexString(payload));
        payload[0] = 0;
        Assert.Equal(ProtocolSamples.PowerOffFrame, Convert.ToHexString(frame));
    }
}
