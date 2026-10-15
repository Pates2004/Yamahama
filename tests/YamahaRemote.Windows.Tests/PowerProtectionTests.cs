using System.IO;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class PowerProtectionTests
{
    private static readonly SoundbarState Standby = new(false, SoundbarInput.Tv, 10, false, 0,
        SoundMode.Standard, false, false, IndicatorBrightness.Bright, DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"FirstRunCompleted\":true,\"Language\":\"en\",\"Theme\":\"Dark\"}")]
    public void NewAndExistingConfigurationsKeepProtectionOff(string json)
    {
        string path = ConfigurationTests.TestPath("power-protection-default.json");
        File.WriteAllText(path, json);
        Assert.False(UiPreferences.Load(out string? warning, path).KeepPoweredOn);
        Assert.Null(warning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreferenceRoundTripsIndependentlyOfStartup(bool enabled)
    {
        string path = ConfigurationTests.TestPath($"power-protection-{enabled}.json");
        Assert.True(new UiPreferences { KeepPoweredOn = enabled }.TrySave(out _, path));
        UiPreferences saved = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        Assert.Equal(enabled, saved.KeepPoweredOn);
        Assert.True(saved.AutoConnectAtStartup);
        Assert.False(saved.PowerOnAtStartup);
        Assert.False(saved.PowerOffOnExit);
        Assert.Empty(saved.SavedDevices);
    }

    [Fact]
    public void StandbyOnlyArmsAfterAnObservedPoweredOnStateAndResetsBetweenSessions()
    {
        PowerProtectionPolicy policy = new(new ProtectionTestClock());
        policy.Observe(Standby);
        Assert.False(policy.ShouldRestore(Standby));
        policy.Observe(Standby with { Power = true });
        Assert.False(policy.ShouldRestore(Standby with { Power = true }));
        Assert.True(policy.ShouldRestore(Standby));
        policy.Reset();
        Assert.False(policy.ShouldRestore(Standby));
    }

    [Fact]
    public void PollingUsesMonotonicFifteenSecondIntervals()
    {
        ProtectionTestClock clock = new();
        PowerProtectionPolicy policy = new(clock);
        Assert.True(policy.ShouldCheck(null, false));
        policy.BeginCheck();
        Assert.False(policy.ShouldCheck(Standby, true));
        clock.Advance(TimeSpan.FromSeconds(14));
        Assert.False(policy.ShouldCheck(Standby, true));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(policy.ShouldCheck(Standby, true));
    }

    [Fact]
    public void NotificationTriggersAnEarlierCheckButStaleStateDoesNot()
    {
        PowerProtectionPolicy policy = new(new ProtectionTestClock());
        policy.Observe(Standby with { Power = true });
        policy.BeginCheck();
        Assert.False(policy.ShouldCheck(Standby, false));
        Assert.True(policy.ShouldCheck(Standby, true));
    }

    [Fact]
    public void RepeatedRestoresAndFailuresAreRateLimitedAndDoNotFloodErrors()
    {
        ProtectionTestClock clock = new();
        PowerProtectionPolicy policy = new(clock);
        policy.Observe(Standby with { Power = true });
        policy.BeginRestore();
        Assert.True(policy.Fail());
        Assert.False(policy.Fail());
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(policy.ShouldCheck(Standby, true));
        Assert.False(policy.ShouldRestore(Standby));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(policy.ShouldCheck(Standby, true));
        Assert.True(policy.ShouldRestore(Standby));
        policy.Succeed();
        Assert.True(policy.Fail());
        policy.Reset();
        Assert.True(policy.ShouldCheck(Standby, true));
        Assert.False(policy.ShouldRestore(Standby));
    }
}

internal sealed class ProtectionTestClock : TimeProvider
{
    private long _timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public void Advance(TimeSpan interval) => _timestamp += interval.Ticks;
}
