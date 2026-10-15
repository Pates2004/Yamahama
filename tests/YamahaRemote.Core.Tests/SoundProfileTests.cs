using System.Text.Json;
using System.Text.Json.Serialization;
using YamahaRemote.Core;
using Xunit;

namespace YamahaRemote.Core.Tests;

public sealed class SoundProfileTests
{
    private static SoundbarState InitialState => new(true, SoundbarInput.Optical, 37, true, -4,
        SoundMode.Stereo, false, false, IndicatorBrightness.Dim, new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

    private static SoundProfile TargetProfile => new(SoundMode.Game, true, true, 4);

    public static TheoryData<SoundMode, bool, bool, int> ValidProfiles
    {
        get
        {
            TheoryData<SoundMode, bool, bool, int> profiles = new();
            foreach (SoundMode mode in new[] { SoundMode.Stereo, SoundMode.Standard, SoundMode.Movie3D, SoundMode.Game })
                foreach (bool clearVoice in new[] { false, true })
                    foreach (bool bassExtension in new[] { false, true })
                        for (int subwoofer = -4; subwoofer <= 4; subwoofer++)
                            profiles.Add(mode, clearVoice, bassExtension, subwoofer);
            return profiles;
        }
    }

    public static TheoryData<string, SoundProfileFailure> OutsideChanges => new()
    {
        { nameof(SoundbarState.Power), SoundProfileFailure.Standby },
        { nameof(SoundbarState.Input), SoundProfileFailure.UnexpectedState },
        { nameof(SoundbarState.Volume), SoundProfileFailure.UnexpectedState },
        { nameof(SoundbarState.Muted), SoundProfileFailure.UnexpectedState },
        { nameof(SoundbarState.Brightness), SoundProfileFailure.UnexpectedState },
        { nameof(SoundbarState.Mode), SoundProfileFailure.UnexpectedState },
        { nameof(SoundbarState.ClearVoice), SoundProfileFailure.UnexpectedState },
        { nameof(SoundbarState.BassExtension), SoundProfileFailure.UnexpectedState },
        { nameof(SoundbarState.Subwoofer), SoundProfileFailure.UnexpectedState }
    };

    public static TheoryData<string, SoundProfileFailure> StateFailures => new()
    {
        { "Disconnected", SoundProfileFailure.NotConnected },
        { "Missing", SoundProfileFailure.InvalidState },
        { "InvalidMode", SoundProfileFailure.InvalidState },
        { "Standby", SoundProfileFailure.Standby },
        { "Stale", SoundProfileFailure.StaleState },
        { "Older", SoundProfileFailure.StaleState }
    };

    [Theory]
    [MemberData(nameof(ValidProfiles))]
    public void ValidateAcceptsAllSupportedProfiles(SoundMode mode, bool clearVoice, bool bassExtension, int subwoofer)
    {
        new SoundProfile(mode, clearVoice, bassExtension, subwoofer).Validate();
    }

    [Theory]
    [InlineData(SoundMode.Unknown)]
    [InlineData((SoundMode)(-1))]
    [InlineData((SoundMode)5)]
    [InlineData((SoundMode)int.MinValue)]
    [InlineData((SoundMode)int.MaxValue)]
    public async Task InvalidProfileModesAreRejectedBeforeControllerAccess(SoundMode mode)
    {
        SoundProfile profile = TargetProfile with { Mode = mode };
        FakeSoundbarController controller = new() { IsConnected = false };

        Assert.Throws<ArgumentException>(nameof(SoundProfile.Mode), profile.Validate);
        await Assert.ThrowsAsync<ArgumentException>(() => SoundProfileOperations.ApplyAsync(controller, profile, CancellationToken.None));
        AssertNoAccess(controller);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(5)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public async Task InvalidProfileSubwooferIsRejectedBeforeControllerAccess(int subwoofer)
    {
        SoundProfile profile = TargetProfile with { Subwoofer = subwoofer };
        FakeSoundbarController controller = new();

        Assert.Throws<ArgumentException>(nameof(SoundProfile.Subwoofer), profile.Validate);
        await Assert.ThrowsAsync<ArgumentException>(() => SoundProfileOperations.ApplyAsync(controller, profile, CancellationToken.None));
        AssertNoAccess(controller);
    }

    [Fact]
    public async Task NullArgumentsAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>("controller", () => SoundProfileOperations.CaptureAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>("controller", () => SoundProfileOperations.ApplyAsync(null!, TargetProfile, CancellationToken.None));
        FakeSoundbarController controller = new();
        await Assert.ThrowsAsync<ArgumentNullException>("profile", () => SoundProfileOperations.ApplyAsync(controller, null!, CancellationToken.None));
        AssertNoAccess(controller);
    }

    [Theory]
    [InlineData(SoundProfileFailure.NotConnected)]
    [InlineData(SoundProfileFailure.StaleState)]
    [InlineData(SoundProfileFailure.InvalidState)]
    [InlineData(SoundProfileFailure.Standby)]
    [InlineData(SoundProfileFailure.UnexpectedState)]
    public void ExceptionExposesItsFailureAndEnglishMessage(SoundProfileFailure failure)
    {
        SoundProfileException exception = new(failure);

        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Equal(failure, exception.Failure);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    [Theory]
    [MemberData(nameof(ValidProfiles))]
    public async Task CaptureReadsFreshProfileWithoutMutatingEvenInStandby(SoundMode mode, bool clearVoice, bool bassExtension, int subwoofer)
    {
        foreach (bool power in new[] { false, true })
        {
            FakeSoundbarController controller = new();
            SoundbarState refreshed = InitialState with
            {
                Power = power, Mode = mode, ClearVoice = clearVoice, BassExtension = bassExtension,
                Subwoofer = subwoofer, ReceivedAt = InitialState.ReceivedAt.AddTicks(1)
            };
            controller.OnRefresh = (_, _) =>
            {
                controller.ReportedState = refreshed;
                return Task.CompletedTask;
            };

            SoundProfile profile = await SoundProfileOperations.CaptureAsync(controller, CancellationToken.None);

            Assert.Equal(new SoundProfile(mode, clearVoice, bassExtension, subwoofer), profile);
            Assert.Equal(refreshed, controller.ReportedState);
            Assert.Equal(1, controller.RefreshCount);
            Assert.Empty(controller.Commands);
        }
    }

    [Theory]
    [MemberData(nameof(ValidProfiles))]
    public async Task ApplyConfirmsEachStepAndPreservesEverythingElse(SoundMode mode, bool clearVoice, bool bassExtension, int subwoofer)
    {
        SoundbarState initial = InitialState with
        {
            Mode = mode == SoundMode.Stereo ? SoundMode.Standard : SoundMode.Stereo,
            ClearVoice = !clearVoice, BassExtension = !bassExtension, Subwoofer = subwoofer < 0 ? 4 : -4
        };
        FakeSoundbarController controller = new() { ReportedState = initial };
        SoundProfile profile = new(mode, clearVoice, bassExtension, subwoofer);
        using CancellationTokenSource cancellation = new();

        await SoundProfileOperations.ApplyAsync(controller, profile, cancellation.Token);

        int steps = Math.Abs(subwoofer - initial.Subwoofer);
        SoundbarCommand direction = subwoofer > initial.Subwoofer ? SoundbarCommand.SubwooferUp : SoundbarCommand.SubwooferDown;
        SoundbarCommand[] expectedCommands =
        [
            ModeCommand(mode),
            clearVoice ? SoundbarCommand.ClearVoiceOn : SoundbarCommand.ClearVoiceOff,
            bassExtension ? SoundbarCommand.BassExtensionOn : SoundbarCommand.BassExtensionOff,
            .. Enumerable.Repeat(direction, steps)
        ];
        Assert.Equal(expectedCommands, controller.Commands);
        Assert.InRange(steps, 0, 8);
        Assert.Equal(2, controller.RefreshCount);
        Assert.All(controller.Tokens, token => Assert.Equal(cancellation.Token, token));
        AssertState(initial, controller.BeforeCommands[0]);
        AssertState(initial with { Mode = mode }, controller.BeforeCommands[1]);
        AssertState(initial with { Mode = mode, ClearVoice = clearVoice }, controller.BeforeCommands[2]);
        for (int stepIndex = 0; stepIndex < steps; stepIndex++)
        {
            AssertState(initial with
            {
                Mode = mode, ClearVoice = clearVoice, BassExtension = bassExtension,
                Subwoofer = initial.Subwoofer + Math.Sign(subwoofer - initial.Subwoofer) * stepIndex
            }, controller.BeforeCommands[stepIndex + 3]);
        }
        AssertState(initial with { Mode = mode, ClearVoice = clearVoice, BassExtension = bassExtension, Subwoofer = subwoofer }, controller.ReportedState);
    }

    [Theory]
    [InlineData(SoundMode.Stereo, SoundbarCommand.Stereo)]
    [InlineData(SoundMode.Standard, SoundbarCommand.Standard)]
    [InlineData(SoundMode.Movie3D, SoundbarCommand.Movie3D)]
    [InlineData(SoundMode.Game, SoundbarCommand.Game)]
    public async Task ModesUseExplicitNamedCommands(SoundMode mode, SoundbarCommand command)
    {
        FakeSoundbarController controller = new()
        {
            ReportedState = InitialState with { Mode = mode == SoundMode.Stereo ? SoundMode.Game : SoundMode.Stereo }
        };

        await SoundProfileOperations.ApplyAsync(controller, ProfileOf(InitialState) with { Mode = mode }, CancellationToken.None);

        Assert.Equal(command, Assert.Single(controller.Commands));
    }

    [Theory]
    [MemberData(nameof(ValidProfiles))]
    public async Task NoOpStillRefreshesTwiceAndSendsNothing(SoundMode mode, bool clearVoice, bool bassExtension, int subwoofer)
    {
        SoundbarState initial = InitialState with { Mode = mode, ClearVoice = clearVoice, BassExtension = bassExtension, Subwoofer = subwoofer };
        FakeSoundbarController controller = new() { ReportedState = initial };

        await SoundProfileOperations.ApplyAsync(controller, ProfileOf(initial), CancellationToken.None);

        Assert.Equal(2, controller.RefreshCount);
        Assert.Empty(controller.Commands);
        AssertState(initial, controller.ReportedState);
        Assert.True(controller.ReportedState!.ReceivedAt > initial.ReceivedAt);
    }

    [Fact]
    public async Task ApplyPreservesEverySupportedInputBrightnessVolumeBoundaryAndMuteState()
    {
        foreach (SoundbarInput input in Enum.GetValues<SoundbarInput>())
            foreach (IndicatorBrightness brightness in Enum.GetValues<IndicatorBrightness>())
                foreach (int volume in new[] { 0, 50 })
                    foreach (bool muted in new[] { false, true })
                    {
                        SoundbarState initial = InitialState with { Input = input, Brightness = brightness, Volume = volume, Muted = muted };
                        FakeSoundbarController controller = new() { ReportedState = initial };

                        await SoundProfileOperations.ApplyAsync(controller, TargetProfile, CancellationToken.None);

                        Assert.All(controller.BeforeCommands.Append(controller.ReportedState!), state =>
                        {
                            Assert.Equal(initial.Power, state.Power);
                            Assert.Equal(input, state.Input);
                            Assert.Equal(volume, state.Volume);
                            Assert.Equal(muted, state.Muted);
                            Assert.Equal(brightness, state.Brightness);
                        });
                    }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialRefreshCanReplaceMissingOrInvalidCachedState(bool apply)
    {
        foreach (SoundbarState? cached in new SoundbarState?[] { null, InitialState with { Mode = SoundMode.Unknown, Power = false } })
        {
            FakeSoundbarController controller = new() { ReportedState = cached };
            controller.OnRefresh = (_, _) =>
            {
                controller.ReportedState = InitialState with { ReceivedAt = InitialState.ReceivedAt.AddTicks(controller.RefreshCount) };
                return Task.CompletedTask;
            };

            if (apply)
                await SoundProfileOperations.ApplyAsync(controller, ProfileOf(InitialState), CancellationToken.None);
            else
                Assert.Equal(ProfileOf(InitialState), await SoundProfileOperations.CaptureAsync(controller, CancellationToken.None));

            Assert.Empty(controller.Commands);
        }
    }

    [Fact]
    public async Task ApplyUsesRefreshedValuesAsItsPreservationBaseline()
    {
        FakeSoundbarController controller = new() { ReportedState = InitialState with { Power = false } };
        SoundbarState refreshed = InitialState with { Input = SoundbarInput.Analog, Volume = 3, Muted = false, Brightness = IndicatorBrightness.Off };
        controller.OnRefresh = (_, _) =>
        {
            if (controller.RefreshCount == 1)
                controller.ReportedState = refreshed;
            controller.AdvanceTimestamp();
            return Task.CompletedTask;
        };

        await SoundProfileOperations.ApplyAsync(controller, TargetProfile, CancellationToken.None);

        AssertState(refreshed with { Mode = TargetProfile.Mode, ClearVoice = true, BassExtension = true, Subwoofer = 4 }, controller.ReportedState);
        Assert.DoesNotContain(SoundbarCommand.PowerOn, controller.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectedControllerFailsWithoutRefresh(bool apply)
    {
        FakeSoundbarController controller = new() { IsConnected = false };

        await AssertFailure(SoundProfileFailure.NotConnected, () => RunOperationAsync(controller, apply));

        AssertNoAccess(controller);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectDuringInitialRefreshStopsTheOperation(bool apply)
    {
        FakeSoundbarController controller = new();
        controller.OnRefresh = (_, _) =>
        {
            controller.AdvanceTimestamp();
            controller.IsConnected = false;
            return Task.CompletedTask;
        };

        await AssertFailure(SoundProfileFailure.NotConnected, () => RunOperationAsync(controller, apply));

        Assert.Equal(1, controller.RefreshCount);
        Assert.Empty(controller.Commands);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("UnknownMode")]
    [InlineData("UndefinedMode")]
    [InlineData("Input")]
    [InlineData("LowVolume")]
    [InlineData("HighVolume")]
    [InlineData("LowSubwoofer")]
    [InlineData("HighSubwoofer")]
    [InlineData("Brightness")]
    public async Task InvalidRefreshedStateIsRejectedForCaptureAndApply(string invalidField)
    {
        foreach (bool apply in new[] { false, true })
        {
            FakeSoundbarController controller = new();
            controller.OnRefresh = (_, _) =>
            {
                controller.AdvanceTimestamp();
                SoundbarState state = controller.ReportedState!;
                controller.ReportedState = invalidField switch
                {
                    "Missing" => null,
                    "UnknownMode" => state with { Mode = SoundMode.Unknown },
                    "UndefinedMode" => state with { Mode = (SoundMode)100 },
                    "Input" => state with { Input = (SoundbarInput)0 },
                    "LowVolume" => state with { Volume = -1 },
                    "HighVolume" => state with { Volume = 51 },
                    "LowSubwoofer" => state with { Subwoofer = -5 },
                    "HighSubwoofer" => state with { Subwoofer = 5 },
                    "Brightness" => state with { Brightness = (IndicatorBrightness)3 },
                    _ => throw new ArgumentException("Unknown test case.", nameof(invalidField))
                };
                return Task.CompletedTask;
            };

            await AssertFailure(SoundProfileFailure.InvalidState, () => RunOperationAsync(controller, apply));

            Assert.Equal(1, controller.RefreshCount);
            Assert.Empty(controller.Commands);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task InitialRefreshRequiresStrictlyNewerTimestamp(int ticks)
    {
        foreach (bool apply in new[] { false, true })
        {
            FakeSoundbarController controller = new();
            controller.OnRefresh = (_, _) =>
            {
                controller.ReportedState = InitialState with { ReceivedAt = InitialState.ReceivedAt.AddTicks(ticks) };
                return Task.CompletedTask;
            };

            await AssertFailure(SoundProfileFailure.StaleState, () => RunOperationAsync(controller, apply));

            Assert.Equal(1, controller.RefreshCount);
            Assert.Empty(controller.Commands);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingCachedStateDoesNotMakeDefaultTimestampFresh(bool apply)
    {
        FakeSoundbarController controller = new() { ReportedState = null };
        controller.OnRefresh = (_, _) =>
        {
            controller.ReportedState = InitialState with { ReceivedAt = default };
            return Task.CompletedTask;
        };

        await AssertFailure(SoundProfileFailure.StaleState, () => RunOperationAsync(controller, apply));

        Assert.Empty(controller.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyNeverPowersOnEvenForANoOpProfile(bool noOp)
    {
        FakeSoundbarController controller = new();
        controller.OnRefresh = (_, _) =>
        {
            controller.ReportedState = InitialState with { Power = false, ReceivedAt = InitialState.ReceivedAt.AddTicks(1) };
            return Task.CompletedTask;
        };

        await AssertFailure(SoundProfileFailure.Standby,
            () => SoundProfileOperations.ApplyAsync(controller, noOp ? ProfileOf(InitialState) : TargetProfile, CancellationToken.None));

        Assert.Equal(1, controller.RefreshCount);
        Assert.Empty(controller.Commands);
        Assert.False(controller.ReportedState!.Power);
    }

    [Theory]
    [MemberData(nameof(OutsideChanges))]
    public async Task OutsideChangesDuringACommandStopWithoutUndoingAnything(string field, SoundProfileFailure failure)
    {
        FakeSoundbarController controller = new();
        SoundbarState? outsideState = null;
        controller.OnSend = (_, command, _) =>
        {
            controller.Confirm(command);
            outsideState = ChangeField(controller.ReportedState!, field);
            controller.ReportedState = outsideState;
            return Task.CompletedTask;
        };

        await AssertFailure(failure, () => RunOperationAsync(controller, true));

        Assert.Equal(SoundbarCommand.Game, Assert.Single(controller.Commands));
        Assert.Equal(1, controller.RefreshCount);
        Assert.Equal(outsideState, controller.ReportedState);
    }

    [Theory]
    [MemberData(nameof(OutsideChanges))]
    public async Task PreCommandChangesAreDetectedBeforeTheFirstOrNextSend(string field, SoundProfileFailure failure)
    {
        foreach (int readNumber in new[] { 3, 5 })
            foreach (bool newerTimestamp in new[] { false, true })
            {
                FakeSoundbarController controller = new();
                SoundbarState? outsideState = null;
                controller.OnStateRead = current =>
                {
                    if (current.StateReadCount != readNumber)
                        return;
                    outsideState = ChangeField(current.ReportedState!, field);
                    if (newerTimestamp)
                        outsideState = outsideState with { ReceivedAt = outsideState.ReceivedAt.AddTicks(1) };
                    current.ReportedState = outsideState;
                };

                await AssertFailure(failure, () => RunOperationAsync(controller, true));

                Assert.Equal(readNumber == 3 ? 0 : 1, controller.Commands.Count);
                Assert.Equal(1, controller.RefreshCount);
                Assert.Equal(outsideState, controller.ReportedState);
            }
    }

    [Theory]
    [MemberData(nameof(StateFailures))]
    public async Task InvalidCommandConfirmationStopsWithoutRetries(string fault, SoundProfileFailure failure)
    {
        FakeSoundbarController controller = new();
        controller.OnSend = (_, command, _) =>
        {
            DateTimeOffset previous = controller.ReportedState!.ReceivedAt;
            controller.Confirm(command);
            InjectFault(controller, fault, previous);
            return Task.CompletedTask;
        };

        await AssertFailure(failure, () => RunOperationAsync(controller, true));

        Assert.Equal(SoundbarCommand.Game, Assert.Single(controller.Commands));
        Assert.Equal(1, controller.RefreshCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public async Task EveryCommandNeedsAnExactConfirmationAndFailureLeavesPartialProgress(int failedCommand)
    {
        FakeSoundbarController controller = new();
        SoundbarState? partialState = null;
        controller.OnSend = (_, command, _) =>
        {
            if (controller.Commands.Count == failedCommand)
            {
                controller.AdvanceTimestamp();
                partialState = controller.ReportedState;
            }
            else
                controller.Confirm(command);
            return Task.CompletedTask;
        };

        await AssertFailure(SoundProfileFailure.UnexpectedState, () => RunOperationAsync(controller, true));

        Assert.Equal(failedCommand, controller.Commands.Count);
        Assert.Equal(1, controller.RefreshCount);
        Assert.Equal(partialState, controller.ReportedState);
        Assert.Equal(failedCommand > 1 ? SoundMode.Game : SoundMode.Stereo, partialState!.Mode);
        Assert.Equal(failedCommand > 2, partialState.ClearVoice);
        Assert.Equal(failedCommand > 3, partialState.BassExtension);
        Assert.Equal(-4 + Math.Max(0, failedCommand - 4), partialState.Subwoofer);
    }

    [Theory]
    [InlineData(-4, 4, -2)]
    [InlineData(4, -4, 2)]
    [InlineData(0, 4, -1)]
    [InlineData(0, -4, 1)]
    [InlineData(-4, 4, 4)]
    [InlineData(4, -4, -4)]
    public async Task RelativeSubwooferCannotSkipStepsOrMoveInTheWrongDirection(int initial, int target, int reported)
    {
        FakeSoundbarController controller = new() { ReportedState = InitialState with { Subwoofer = initial } };
        controller.OnSend = (_, _, _) =>
        {
            controller.ReportedState = controller.ReportedState! with { Subwoofer = reported };
            controller.AdvanceTimestamp();
            return Task.CompletedTask;
        };

        await AssertFailure(SoundProfileFailure.UnexpectedState,
            () => SoundProfileOperations.ApplyAsync(controller, ProfileOf(InitialState) with { Subwoofer = target }, CancellationToken.None));

        Assert.Equal(target > initial ? SoundbarCommand.SubwooferUp : SoundbarCommand.SubwooferDown, Assert.Single(controller.Commands));
        Assert.Equal(reported, controller.ReportedState!.Subwoofer);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public async Task RegressingPreCommandTimestampIsRejectedEvenWhenFieldsMatch(int readNumber)
    {
        FakeSoundbarController controller = new();
        controller.OnStateRead = current =>
        {
            if (current.StateReadCount == readNumber)
                current.ReportedState = current.ReportedState! with { ReceivedAt = current.ReportedState.ReceivedAt.AddTicks(-1) };
        };

        await AssertFailure(SoundProfileFailure.StaleState, () => RunOperationAsync(controller, true));

        Assert.Equal(readNumber == 3 ? 0 : 1, controller.Commands.Count);
        Assert.Equal(1, controller.RefreshCount);
    }

    [Fact]
    public async Task TimestampOnlyPollingBetweenStepsDoesNotCountAsAnOutsideChange()
    {
        FakeSoundbarController controller = new();
        controller.OnStateRead = current => current.AdvanceTimestamp();

        await RunOperationAsync(controller, true);

        Assert.Equal(11, controller.Commands.Count);
        Assert.Equal(2, controller.RefreshCount);
        Assert.Equal(TargetProfile, ProfileOf(controller.ReportedState!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshnessIsComparedWithTheImmediatePreOperationSnapshot(bool noOp)
    {
        FakeSoundbarController controller = new();
        controller.OnStateRead = current =>
        {
            if (current.StateReadCount == 3)
                current.ReportedState = current.ReportedState! with { ReceivedAt = current.ReportedState.ReceivedAt.AddTicks(100) };
        };
        if (noOp)
        {
            controller.OnRefresh = (_, _) =>
            {
                controller.ReportedState = InitialState with { ReceivedAt = InitialState.ReceivedAt.AddTicks(controller.RefreshCount) };
                return Task.CompletedTask;
            };
        }
        else
        {
            controller.OnSend = (_, command, _) =>
            {
                controller.Confirm(command);
                controller.ReportedState = controller.ReportedState! with { ReceivedAt = InitialState.ReceivedAt.AddTicks(2) };
                return Task.CompletedTask;
            };
        }

        await AssertFailure(SoundProfileFailure.StaleState,
            () => SoundProfileOperations.ApplyAsync(controller, noOp ? ProfileOf(InitialState) : TargetProfile, CancellationToken.None));

        Assert.Equal(noOp ? 0 : 1, controller.Commands.Count);
        Assert.Equal(noOp ? 2 : 1, controller.RefreshCount);
    }

    [Theory]
    [MemberData(nameof(OutsideChanges))]
    public async Task FinalRefreshChecksEveryTargetAndPreservedFieldEvenForNoOp(string field, SoundProfileFailure failure)
    {
        foreach (bool noOp in new[] { false, true })
        {
            FakeSoundbarController controller = new();
            SoundbarState? outsideState = null;
            controller.OnRefresh = (_, _) =>
            {
                controller.AdvanceTimestamp();
                if (controller.RefreshCount == 2)
                {
                    outsideState = ChangeField(controller.ReportedState!, field);
                    controller.ReportedState = outsideState;
                }
                return Task.CompletedTask;
            };

            await AssertFailure(failure,
                () => SoundProfileOperations.ApplyAsync(controller, noOp ? ProfileOf(InitialState) : TargetProfile, CancellationToken.None));

            Assert.Equal(2, controller.RefreshCount);
            Assert.Equal(noOp ? 0 : 11, controller.Commands.Count);
            Assert.Equal(outsideState, controller.ReportedState);
        }
    }

    [Theory]
    [MemberData(nameof(StateFailures))]
    public async Task FinalRefreshMustRemainConnectedValidOnAndFresh(string fault, SoundProfileFailure failure)
    {
        foreach (bool noOp in new[] { false, true })
        {
            FakeSoundbarController controller = new();
            controller.OnRefresh = (_, _) =>
            {
                DateTimeOffset previous = controller.ReportedState!.ReceivedAt;
                controller.AdvanceTimestamp();
                if (controller.RefreshCount == 2)
                    InjectFault(controller, fault, previous);
                return Task.CompletedTask;
            };

            await AssertFailure(failure,
                () => SoundProfileOperations.ApplyAsync(controller, noOp ? ProfileOf(InitialState) : TargetProfile, CancellationToken.None));

            Assert.Equal(2, controller.RefreshCount);
            Assert.Equal(noOp ? 0 : 11, controller.Commands.Count);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangesBeforeTheFinalRefreshAreNotHiddenByAnotherRead(bool noOp)
    {
        FakeSoundbarController controller = new();
        controller.OnStateRead = current =>
        {
            if (current.StateReadCount == (noOp ? 3 : 25))
                current.ReportedState = current.ReportedState! with { Volume = 4 };
        };

        await AssertFailure(SoundProfileFailure.UnexpectedState,
            () => SoundProfileOperations.ApplyAsync(controller, noOp ? ProfileOf(InitialState) : TargetProfile, CancellationToken.None));

        Assert.Equal(1, controller.RefreshCount);
        Assert.Equal(noOp ? 0 : 11, controller.Commands.Count);
        Assert.Equal(4, controller.ReportedState!.Volume);
    }

    [Theory]
    [InlineData("InitialRefresh", false)]
    [InlineData("InitialRefresh", true)]
    [InlineData("Send", true)]
    [InlineData("FinalRefresh", true)]
    [InlineData("StateRead", false)]
    [InlineData("StateRead", true)]
    public async Task ControllerFailuresPropagateUnchangedWithoutRetryOrRollback(string stage, bool apply)
    {
        foreach (Exception error in new Exception[] { new IOException("Read failed."), new TimeoutException("Read timed out."), new OperationCanceledException() })
        {
            FakeSoundbarController controller = new();
            if (stage == "Send")
            {
                controller.OnSend = (_, command, _) =>
                {
                    controller.Confirm(command);
                    return Task.FromException(error);
                };
            }
            else if (stage == "StateRead")
            {
                controller.OnStateRead = current =>
                {
                    if (current.StateReadCount == 2)
                        throw error;
                };
            }
            else
            {
                controller.OnRefresh = (_, _) =>
                {
                    if (controller.RefreshCount == (stage == "InitialRefresh" ? 1 : 2))
                        return Task.FromException(error);
                    controller.AdvanceTimestamp();
                    return Task.CompletedTask;
                };
            }

            Assert.Same(error, await Record.ExceptionAsync(() => RunOperationAsync(controller, apply)));

            Assert.Equal(stage == "Send" ? 1 : stage == "FinalRefresh" ? 11 : 0, controller.Commands.Count);
            Assert.Equal(stage == "FinalRefresh" ? 2 : 1, controller.RefreshCount);
            if (stage == "Send")
                Assert.Equal(SoundMode.Game, controller.ReportedState!.Mode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCanceledOperationDoesNotTouchTheController(bool apply)
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        FakeSoundbarController controller = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunOperationAsync(controller, apply, cancellation.Token));

        AssertNoAccess(controller);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task CancellationAfterRefreshIsObservedEvenIfControllerIgnoresIt(bool apply, int canceledRefresh)
    {
        using CancellationTokenSource cancellation = new();
        FakeSoundbarController controller = new();
        controller.OnRefresh = (_, _) =>
        {
            controller.AdvanceTimestamp();
            if (controller.RefreshCount == canceledRefresh)
                cancellation.Cancel();
            return Task.CompletedTask;
        };

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunOperationAsync(controller, apply, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(canceledRefresh, controller.RefreshCount);
        Assert.Equal(canceledRefresh == 1 ? 0 : 11, controller.Commands.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(11)]
    public async Task CancellationAfterSendLeavesPartialProgressAndSendsNoFurtherCommands(int canceledCommand)
    {
        using CancellationTokenSource cancellation = new();
        FakeSoundbarController controller = new();
        SoundbarState? partialState = null;
        controller.OnSend = (_, command, _) =>
        {
            controller.Confirm(command);
            if (controller.Commands.Count == canceledCommand)
            {
                partialState = controller.ReportedState;
                cancellation.Cancel();
            }
            return Task.CompletedTask;
        };

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunOperationAsync(controller, true, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(canceledCommand, controller.Commands.Count);
        Assert.Equal(1, controller.RefreshCount);
        Assert.Equal(partialState, controller.ReportedState);
        Assert.Equal(SoundMode.Game, partialState!.Mode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InFlightRefreshIsAwaitedAndCancellationIsForwarded(bool apply)
    {
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource refresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeSoundbarController controller = new();
        controller.OnRefresh = async (_, token) =>
        {
            await refresh.Task.WaitAsync(token);
            controller.AdvanceTimestamp();
        };

        Task operation = RunOperationAsync(controller, apply, cancellation.Token);

        Assert.False(operation.IsCompleted);
        Assert.Empty(controller.Commands);
        Assert.Equal(cancellation.Token, Assert.Single(controller.Tokens));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, controller.RefreshCount);
        Assert.Empty(controller.Commands);
    }

    [Fact]
    public async Task InFlightSendIsAwaitedBeforeAnySubsequentCommandOrFinalRefresh()
    {
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeSoundbarController controller = new();
        controller.OnSend = async (_, command, token) =>
        {
            await confirmation.Task.WaitAsync(token);
            controller.Confirm(command);
        };

        Task operation = RunOperationAsync(controller, true, cancellation.Token);

        Assert.False(operation.IsCompleted);
        Assert.Equal(SoundbarCommand.Game, Assert.Single(controller.Commands));
        Assert.Equal(1, controller.RefreshCount);
        confirmation.SetResult();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(11, controller.Commands.Count);
        Assert.Equal(2, controller.RefreshCount);
        Assert.Equal(TargetProfile, ProfileOf(controller.ReportedState!));
    }

    [Fact]
    public void ProfileRoundTripsWithStrictStringEnumAndRequiredConstructorOptions()
    {
        JsonSerializerOptions options = ProfileJsonOptions();

        string json = JsonSerializer.Serialize(TargetProfile, options);
        SoundProfile? restored = JsonSerializer.Deserialize<SoundProfile>(json, options);

        Assert.Contains("\"Mode\":\"Game\"", json);
        Assert.Equal(TargetProfile, restored);
        restored!.Validate();
    }

    [Theory]
    [InlineData(nameof(SoundProfile.Mode))]
    [InlineData(nameof(SoundProfile.ClearVoice))]
    [InlineData(nameof(SoundProfile.BassExtension))]
    [InlineData(nameof(SoundProfile.Subwoofer))]
    public void StrictSerializerRequiresEveryProfileConstructorArgument(string missingProperty)
    {
        Dictionary<string, object> properties = new()
        {
            [nameof(SoundProfile.Mode)] = "Game",
            [nameof(SoundProfile.ClearVoice)] = true,
            [nameof(SoundProfile.BassExtension)] = true,
            [nameof(SoundProfile.Subwoofer)] = 4
        };
        properties.Remove(missingProperty);
        string json = JsonSerializer.Serialize(properties);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SoundProfile>(json, ProfileJsonOptions()));
    }

    [Theory]
    [InlineData("4")]
    [InlineData("\"4\"")]
    [InlineData("\"Unsupported\"")]
    [InlineData("null")]
    public void StrictSerializerRejectsNonNamedOrMissingModeValues(string modeJson)
    {
        string json = $$"""{"Mode":{{modeJson}},"ClearVoice":true,"BassExtension":true,"Subwoofer":4}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SoundProfile>(json, ProfileJsonOptions()));
    }

    private static JsonSerializerOptions ProfileJsonOptions() => new()
    {
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter<SoundMode>(allowIntegerValues: false) }
    };

    private static SoundbarState ChangeField(SoundbarState state, string field) => field switch
    {
        nameof(SoundbarState.Power) => state with { Power = false },
        nameof(SoundbarState.Input) => state with { Input = SoundbarInput.Analog },
        nameof(SoundbarState.Volume) => state with { Volume = 38 },
        nameof(SoundbarState.Muted) => state with { Muted = !state.Muted },
        nameof(SoundbarState.Brightness) => state with { Brightness = IndicatorBrightness.Off },
        nameof(SoundbarState.Mode) => state with { Mode = state.Mode == SoundMode.Game ? SoundMode.Standard : SoundMode.Game },
        nameof(SoundbarState.ClearVoice) => state with { ClearVoice = !state.ClearVoice },
        nameof(SoundbarState.BassExtension) => state with { BassExtension = !state.BassExtension },
        nameof(SoundbarState.Subwoofer) => state with { Subwoofer = state.Subwoofer == 4 ? 3 : state.Subwoofer + 1 },
        _ => throw new ArgumentException("Unknown test field.", nameof(field))
    };

    private static void InjectFault(FakeSoundbarController controller, string fault, DateTimeOffset previous)
    {
        if (fault == "Disconnected")
        {
            controller.IsConnected = false;
            return;
        }
        SoundbarState state = controller.ReportedState!;
        controller.ReportedState = fault switch
        {
            "Missing" => null,
            "InvalidMode" => state with { Mode = SoundMode.Unknown },
            "Standby" => state with { Power = false },
            "Stale" => state with { ReceivedAt = previous },
            "Older" => state with { ReceivedAt = previous.AddTicks(-1) },
            _ => throw new ArgumentException("Unknown test fault.", nameof(fault))
        };
    }

    private static SoundProfile ProfileOf(SoundbarState state) => new(state.Mode, state.ClearVoice, state.BassExtension, state.Subwoofer);

    private static SoundbarCommand ModeCommand(SoundMode mode) => mode switch
    {
        SoundMode.Stereo => SoundbarCommand.Stereo,
        SoundMode.Standard => SoundbarCommand.Standard,
        SoundMode.Movie3D => SoundbarCommand.Movie3D,
        SoundMode.Game => SoundbarCommand.Game,
        _ => throw new ArgumentException("Unknown test mode.", nameof(mode))
    };

    private static Task RunOperationAsync(FakeSoundbarController controller, bool apply, CancellationToken cancellationToken = default)
        => apply ? SoundProfileOperations.ApplyAsync(controller, TargetProfile, cancellationToken)
            : SoundProfileOperations.CaptureAsync(controller, cancellationToken);

    private static async Task AssertFailure(SoundProfileFailure failure, Func<Task> operation)
    {
        SoundProfileException exception = await Assert.ThrowsAsync<SoundProfileException>(operation);
        Assert.Equal(failure, exception.Failure);
    }

    private static void AssertNoAccess(FakeSoundbarController controller)
    {
        Assert.Equal(0, controller.StateReadCount);
        Assert.Equal(0, controller.RefreshCount);
        Assert.Empty(controller.Commands);
    }

    private static void AssertState(SoundbarState expected, SoundbarState? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected, actual with { ReceivedAt = expected.ReceivedAt });
    }

    private sealed class FakeSoundbarController : ISoundbarController
    {
        public bool IsConnected { get; set; } = true;
        public SoundbarState? ReportedState { get; set; } = InitialState;
        public int StateReadCount { get; private set; }
        public int RefreshCount { get; private set; }
        public List<SoundbarCommand> Commands { get; } = [];
        public List<SoundbarState> BeforeCommands { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Action<FakeSoundbarController>? OnStateRead { get; set; }
        public Func<FakeSoundbarController, CancellationToken, Task>? OnRefresh { get; set; }
        public Func<FakeSoundbarController, SoundbarCommand, CancellationToken, Task>? OnSend { get; set; }

        public SoundbarState? State
        {
            get
            {
                StateReadCount++;
                OnStateRead?.Invoke(this);
                return ReportedState;
            }
        }

        public event Action<SoundbarState>? StateChanged { add { } remove { } }
        public event Action<string>? ConnectionLost { add { } remove { } }

        public Task RefreshAsync(CancellationToken cancellationToken)
        {
            RefreshCount++;
            Tokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (OnRefresh is not null)
                return OnRefresh(this, cancellationToken);
            AdvanceTimestamp();
            return Task.CompletedTask;
        }

        public Task SendAsync(SoundbarCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            BeforeCommands.Add(ReportedState!);
            Tokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (OnSend is not null)
                return OnSend(this, command, cancellationToken);
            Confirm(command);
            return Task.CompletedTask;
        }

        public void Confirm(SoundbarCommand command)
        {
            SoundbarState state = ReportedState ?? throw new InvalidOperationException("No fake state is available.");
            ReportedState = command switch
            {
                SoundbarCommand.Stereo => state with { Mode = SoundMode.Stereo },
                SoundbarCommand.Standard => state with { Mode = SoundMode.Standard },
                SoundbarCommand.Movie3D => state with { Mode = SoundMode.Movie3D },
                SoundbarCommand.Game => state with { Mode = SoundMode.Game },
                SoundbarCommand.ClearVoiceOn => state with { ClearVoice = true },
                SoundbarCommand.ClearVoiceOff => state with { ClearVoice = false },
                SoundbarCommand.BassExtensionOn => state with { BassExtension = true },
                SoundbarCommand.BassExtensionOff => state with { BassExtension = false },
                SoundbarCommand.SubwooferUp => state with { Subwoofer = state.Subwoofer + 1 },
                SoundbarCommand.SubwooferDown => state with { Subwoofer = state.Subwoofer - 1 },
                _ => throw new InvalidOperationException("A profile must not send this command.")
            };
            AdvanceTimestamp();
        }

        public void AdvanceTimestamp()
        {
            SoundbarState state = ReportedState ?? throw new InvalidOperationException("No fake state is available.");
            ReportedState = state with { ReceivedAt = state.ReceivedAt.AddTicks(1) };
        }

        public Task<IReadOnlyList<DiscoveredSoundbar>> DiscoverAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SoundbarIdentity> ConnectAsync(DiscoveredSoundbar device, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DisconnectAsync() => throw new NotSupportedException();
        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
