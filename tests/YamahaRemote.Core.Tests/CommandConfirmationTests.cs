using YamahaRemote.Core;
using Xunit;

namespace YamahaRemote.Core.Tests;

public sealed class CommandConfirmationTests
{
    public static TheoryData<SoundbarCommand, int, byte, byte> StateTransitions => new()
    {
        { SoundbarCommand.PowerOn, 2, 0, 1 },
        { SoundbarCommand.PowerOff, 2, 1, 0 },
        { SoundbarCommand.InputTv, 3, 5, 7 },
        { SoundbarCommand.InputOptical, 3, 7, 10 },
        { SoundbarCommand.InputBluetooth, 3, 7, 5 },
        { SoundbarCommand.InputAnalog, 3, 7, 12 },
        { SoundbarCommand.MuteOn, 4, 0, 1 },
        { SoundbarCommand.MuteOff, 4, 1, 0 },
        { SoundbarCommand.SubwooferUp, 6, 16, 20 },
        { SoundbarCommand.SubwooferDown, 6, 16, 12 },
        { SoundbarCommand.Stereo, 10, 0, 1 },
        { SoundbarCommand.Standard, 11, 3, 10 },
        { SoundbarCommand.Movie3D, 11, 10, 3 },
        { SoundbarCommand.Game, 11, 10, 12 },
        { SoundbarCommand.ClearVoiceOn, 12, 0, 4 },
        { SoundbarCommand.ClearVoiceOff, 12, 4, 0 },
        { SoundbarCommand.BassExtensionOn, 12, 0, 32 },
        { SoundbarCommand.BassExtensionOff, 12, 32, 0 },
        { SoundbarCommand.IndicatorsBright, 13, 1, 0 },
        { SoundbarCommand.IndicatorsDim, 13, 0, 1 },
        { SoundbarCommand.IndicatorsOff, 13, 1, 2 }
    };

    [Theory]
    [MemberData(nameof(StateTransitions))]
    public void ConfirmationRequiresTheRequestedReportedValueNotSendingTheCommand(
        SoundbarCommand command, int offset, byte previous, byte requested)
    {
        SoundbarState before = ProtocolSamples.ParseState(ProtocolSamples.StatusWith(offset, previous));
        SoundbarProtocol.Encode(SoundbarProtocol.CommandPayload(command));
        SoundbarState unchanged = ProtocolSamples.ParseState(ProtocolSamples.StatusWith(offset, previous));
        SoundbarState unrelatedChange = unchanged with { Volume = 26 };
        SoundbarState confirmed = ProtocolSamples.ParseState(ProtocolSamples.StatusWith(offset, requested));

        Assert.False(SoundbarProtocol.IsCommandConfirmed(command, before, before));
        Assert.False(SoundbarProtocol.IsCommandConfirmed(command, before, unchanged));
        Assert.False(SoundbarProtocol.IsCommandConfirmed(command, before, unrelatedChange));
        Assert.True(SoundbarProtocol.IsCommandConfirmed(command, before, confirmed));
    }

    [Fact]
    public void ConfirmationScenariosCoverEveryCommandExactlyOnce()
    {
        SoundbarCommand[] covered = StateTransitions.Select(row => (SoundbarCommand)row[0]).ToArray();

        Assert.Equal(covered.Length, covered.Distinct().Count());
        Assert.Equal(Enum.GetValues<SoundbarCommand>().Order(), covered.Order());
    }

    [Theory]
    [InlineData(-4, -3, -4)]
    [InlineData(-3, -2, -4)]
    [InlineData(-2, -1, -3)]
    [InlineData(-1, 0, -2)]
    [InlineData(0, 1, -1)]
    [InlineData(1, 2, 0)]
    [InlineData(2, 3, 1)]
    [InlineData(3, 4, 2)]
    [InlineData(4, 4, 3)]
    public void RelativeSubwooferCommandsRequireExactlyOneStepOrTheSaturatedLimit(
        int previous, int expectedUp, int expectedDown)
    {
        SoundbarState baseline = ProtocolSamples.ParseState(Convert.FromHexString(ProtocolSamples.StatusPayload));
        SoundbarState before = baseline with { Subwoofer = previous };

        for (int reported = -4; reported <= 4; reported++)
        {
            SoundbarState after = baseline with { Subwoofer = reported };
            Assert.Equal(reported == expectedUp,
                SoundbarProtocol.IsCommandConfirmed(SoundbarCommand.SubwooferUp, before, after));
            Assert.Equal(reported == expectedDown,
                SoundbarProtocol.IsCommandConfirmed(SoundbarCommand.SubwooferDown, before, after));
        }
    }

    [Theory]
    [InlineData(SoundbarCommand.Stereo)]
    [InlineData(SoundbarCommand.Standard)]
    [InlineData(SoundbarCommand.Movie3D)]
    [InlineData(SoundbarCommand.Game)]
    public void UnknownModeCannotConfirmAnySoundModeCommand(SoundbarCommand command)
    {
        SoundbarState state = ProtocolSamples.ParseState(ProtocolSamples.StatusWith(11, 0xFF));

        Assert.False(SoundbarProtocol.IsCommandConfirmed(command, state, state));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    [InlineData(int.MaxValue)]
    public void UnknownCommandIsNeverConfirmed(int command)
    {
        SoundbarState state = ProtocolSamples.ParseState(Convert.FromHexString(ProtocolSamples.StatusPayload));

        Assert.False(SoundbarProtocol.IsCommandConfirmed((SoundbarCommand)command, state, state));
    }
}
