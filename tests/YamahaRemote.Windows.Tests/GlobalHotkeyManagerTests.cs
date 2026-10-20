using Xunit;

namespace YamahaRemote.Windows.Tests;

public sealed class GlobalHotkeyManagerTests
{
    private static readonly HotkeyBinding[] OriginalBindings =
        [new(HotkeyAction.TogglePower, "Ctrl+P"), new(HotkeyAction.VolumeUp, "Ctrl+Up")];

    [Fact]
    public void ConstructionStartsDisabledWithoutChangingOperatingSystemRegistrations()
    {
        FakeHotkeyBackend backend = new();
        using GlobalHotkeyManager manager = new(nint.Zero, _ => { }, backend);

        Assert.False(manager.IsEnabled);
        Assert.Empty(backend.RegisterAttempts);
        Assert.Empty(backend.UnregisterAttempts);
    }

    [Fact]
    public void SuccessfulApplyUsesNoRepeatAndDispatchesTheMatchingAction()
    {
        FakeHotkeyBackend backend = new();
        List<HotkeyAction> dispatched = [];
        using GlobalHotkeyManager manager = new(nint.Zero, dispatched.Add, backend);

        Assert.True(manager.TryApply(true, OriginalBindings, out var error));

        Assert.Null(error);
        Assert.True(manager.IsEnabled);
        Assert.Equal(2, backend.Registrations.Count);
        foreach ((int registrationId, FakeRegistration registration) in backend.Registrations)
        {
            Assert.InRange(registrationId, 0, 0xBFFF);
            Assert.Equal(0x4002u, registration.Modifiers);
            Assert.True(Send(manager, registrationId, registration));
        }
        Assert.Equal(new[] { HotkeyAction.TogglePower, HotkeyAction.VolumeUp }, dispatched);
    }

    [Fact]
    public void InvalidConfigurationDoesNotTouchExistingRegistrations()
    {
        FakeHotkeyBackend backend = new();
        List<HotkeyAction> dispatched = [];
        using GlobalHotkeyManager manager = new(nint.Zero, dispatched.Add, backend);
        Assert.True(manager.TryApply(true, OriginalBindings, out _));
        Dictionary<int, FakeRegistration> originals = new(backend.Registrations);

        Assert.False(manager.TryApply(true, [new(HotkeyAction.ToggleMute, "Shift+A")], out var error));

        Assert.NotNull(error);
        Assert.Equal(HotkeyRegistrationErrorReason.InvalidGesture, error.Reason);
        Assert.Equal(2, backend.RegisterAttempts.Count);
        Assert.Empty(backend.UnregisterAttempts);
        AssertRegistrations(originals, backend);
        SendAll(manager, backend);
        Assert.Equal(2, dispatched.Count);
        Assert.True(manager.IsEnabled);
    }

    [Fact]
    public void ConflictRemovesStagedBindingsAndPreservesOldConfigurationWithoutReregistering()
    {
        FakeHotkeyBackend backend = new();
        List<HotkeyAction> dispatched = [];
        using GlobalHotkeyManager manager = new(nint.Zero, dispatched.Add, backend);
        Assert.True(manager.TryApply(true, OriginalBindings, out _));
        Dictionary<int, FakeRegistration> originals = new(backend.Registrations);
        backend.RegisterFailure = (_, _, virtualKey) => virtualKey == 0x59 ? 1409 : 0;

        Assert.False(manager.TryApply(true, [new(HotkeyAction.ToggleMute, "Ctrl+M"),
            new(HotkeyAction.ShowWindow, "Ctrl+Y")], out var error));

        Assert.NotNull(error);
        Assert.Equal(HotkeyRegistrationErrorReason.RegistrationFailed, error.Reason);
        Assert.Equal(HotkeyAction.ShowWindow, error.Action);
        Assert.Equal("Ctrl+Y", error.Gesture);
        Assert.Equal(1409, error.Win32Code);
        Assert.Empty(error.RestorationFailures);
        Assert.True(manager.IsEnabled);
        AssertRegistrations(originals, backend);
        Assert.Single(backend.UnregisterAttempts);
        Assert.DoesNotContain(backend.UnregisterAttempts, originals.ContainsKey);
        SendAll(manager, backend);
        Assert.Equal(new[] { HotkeyAction.TogglePower, HotkeyAction.VolumeUp }, dispatched);
    }

    [Fact]
    public void UnchangedConfigurationMakesNoNativeCalls()
    {
        FakeHotkeyBackend backend = new();
        using GlobalHotkeyManager manager = new(nint.Zero, _ => { }, backend);
        Assert.True(manager.TryApply(true, OriginalBindings, out _));

        Assert.True(manager.TryApply(true, OriginalBindings, out _));

        Assert.Equal(2, backend.RegisterAttempts.Count);
        Assert.Empty(backend.UnregisterAttempts);
    }

    [Fact]
    public void SwappingActionsReusesGesturesWithoutRegistrationConflicts()
    {
        FakeHotkeyBackend backend = new();
        List<HotkeyAction> dispatched = [];
        using GlobalHotkeyManager manager = new(nint.Zero, dispatched.Add, backend);
        Assert.True(manager.TryApply(true, OriginalBindings, out _));

        Assert.True(manager.TryApply(true, [new(HotkeyAction.VolumeUp, "Ctrl+P"),
            new(HotkeyAction.TogglePower, "Ctrl+Up")], out _));

        Assert.Equal(2, backend.RegisterAttempts.Count);
        Assert.Empty(backend.UnregisterAttempts);
        SendAll(manager, backend);
        Assert.Equal(new[] { HotkeyAction.VolumeUp, HotkeyAction.TogglePower }, dispatched);
    }

    [Fact]
    public void NewGesturesAreRegisteredBeforeOldOnesAreRemoved()
    {
        FakeHotkeyBackend backend = new();
        using GlobalHotkeyManager manager = new(nint.Zero, _ => { }, backend);
        Assert.True(manager.TryApply(true, OriginalBindings, out _));
        backend.Operations.Clear();

        Assert.True(manager.TryApply(true, [new(HotkeyAction.ToggleMute, "Ctrl+M")], out _));

        Assert.Equal(new[] { "register", "unregister", "unregister" }, backend.Operations);
        Assert.Equal(0x4Du, Assert.Single(backend.Registrations).Value.VirtualKey);
    }

    [Fact]
    public void DisableUnregistersEverythingEvenWhenSuppliedBindingsAreInvalid()
    {
        FakeHotkeyBackend backend = new();
        List<HotkeyAction> dispatched = [];
        using GlobalHotkeyManager manager = new(nint.Zero, dispatched.Add, backend);
        Assert.True(manager.TryApply(true, OriginalBindings, out _));
        Dictionary<int, FakeRegistration> originals = new(backend.Registrations);

        Assert.True(manager.TryApply(false, [new(HotkeyAction.TogglePower, "Shift+A")], out var error));

        Assert.Null(error);
        Assert.False(manager.IsEnabled);
        Assert.Empty(backend.Registrations);
        foreach ((int registrationId, FakeRegistration registration) in originals)
            Assert.False(Send(manager, registrationId, registration));
        Assert.Empty(dispatched);
    }

    [Fact]
    public void EmptyBindingsCanBeEnabledWithoutRegistrations()
    {
        FakeHotkeyBackend backend = new();
        using GlobalHotkeyManager manager = new(nint.Zero, _ => { }, backend);

        Assert.True(manager.TryApply(true, [new(HotkeyAction.ShowWindow, "")], out var error));

        Assert.Null(error);
        Assert.True(manager.IsEnabled);
        Assert.Empty(backend.Registrations);
    }

    [Fact]
    public void NestedCaptureSuspensionsSuppressDispatchAndResumeOnlyAfterBothEnd()
    {
        FakeHotkeyBackend backend = new();
        List<HotkeyAction> dispatched = [];
        using GlobalHotkeyManager manager = new(nint.Zero, dispatched.Add, backend);
        Assert.True(manager.TryApply(true, OriginalBindings, out _));
        IDisposable outer = manager.Suspend();
        IDisposable inner = manager.Suspend();

        SendAll(manager, backend);
        outer.Dispose();
        outer.Dispose();
        SendAll(manager, backend);
        Assert.Empty(dispatched);
        inner.Dispose();
        SendAll(manager, backend);

        Assert.Equal(2, dispatched.Count);
        Assert.True(manager.IsEnabled);
        Assert.Empty(backend.UnregisterAttempts);
    }

    [Fact]
    public void CaptureDisableAndRestoreDiscardQueuedMessagesFromTheOldRegistrations()
    {
        FakeHotkeyBackend backend = new();
        List<HotkeyAction> dispatched = [];
        using GlobalHotkeyManager manager = new(nint.Zero, dispatched.Add, backend);
        Assert.True(manager.TryApply(true, OriginalBindings, out _));
        Dictionary<int, FakeRegistration> old = new(backend.Registrations);

        using (manager.Suspend())
        {
            Assert.True(manager.TryApply(false, OriginalBindings, out _));
            Assert.Empty(backend.Registrations);
            Assert.True(manager.TryApply(true, OriginalBindings, out _));
            SendAll(manager, backend);
            Assert.Empty(dispatched);
        }
        foreach ((int registrationId, FakeRegistration registration) in old)
            Assert.False(Send(manager, registrationId, registration));
        Assert.Empty(dispatched);
        SendAll(manager, backend);
        Assert.Equal(2, dispatched.Count);
    }

    [Fact]
    public void UnknownIdentifiersOtherMessagesAndMismatchedGesturesNeverDispatch()
    {
        FakeHotkeyBackend backend = new();
        List<HotkeyAction> dispatched = [];
        using GlobalHotkeyManager manager = new(nint.Zero, dispatched.Add, backend);
        Assert.True(manager.TryApply(true, OriginalBindings, out _));
        KeyValuePair<int, FakeRegistration> first = backend.Registrations.First();
        bool handled = false;

        manager.ProcessWindowMessage(nint.Zero, 0x100, first.Key, Pack(first.Value), ref handled);
        Assert.False(handled);
        Assert.False(Send(manager, -1, first.Value));
        Assert.False(Send(manager, 0xC000, first.Value));
        Assert.True(Send(manager, first.Key, first.Value with { VirtualKey = 0x4D }));
        Assert.True(Send(manager, first.Key, first.Value with { Modifiers = 1 }));
        Assert.Empty(dispatched);
    }

    [Fact]
    public void UnregistrationFailureRollsBackAndRestoresOldDispatch()
    {
        FakeHotkeyBackend backend = new();
        List<HotkeyAction> dispatched = [];
        using GlobalHotkeyManager manager = new(nint.Zero, dispatched.Add, backend);
        Assert.True(manager.TryApply(true, OriginalBindings, out _));
        Dictionary<int, FakeRegistration> originals = new(backend.Registrations);
        int retainedId = originals.Last().Key;
        backend.UnregisterFailure = registrationId => registrationId == retainedId ? 5 : 0;

        Assert.False(manager.TryApply(true, [new(HotkeyAction.ToggleMute, "Ctrl+M")], out var error));

        Assert.NotNull(error);
        Assert.Equal(HotkeyRegistrationErrorReason.UnregistrationFailed, error.Reason);
        Assert.Equal(5, error.Win32Code);
        Assert.Empty(error.RestorationFailures);
        Assert.True(manager.IsEnabled);
        AssertRegistrations(originals, backend);
        SendAll(manager, backend);
        Assert.Contains(HotkeyAction.TogglePower, dispatched);
        Assert.Contains(HotkeyAction.VolumeUp, dispatched);
        backend.UnregisterFailure = null;
    }

    [Fact]
    public void FailedRestorationIsReportedAndPartialConfigurationNeverDispatches()
    {
        FakeHotkeyBackend backend = new();
        List<HotkeyAction> dispatched = [];
        using GlobalHotkeyManager manager = new(nint.Zero, dispatched.Add, backend);
        Assert.True(manager.TryApply(true, OriginalBindings, out _));
        int retainedId = backend.Registrations.Last().Key;
        backend.UnregisterFailure = registrationId => registrationId == retainedId ? 5 : 0;
        backend.RegisterFailure = (_, _, virtualKey) => virtualKey == 0x50 ? 1409 : 0;

        Assert.False(manager.TryApply(false, OriginalBindings, out var error));

        Assert.NotNull(error);
        Assert.Equal(HotkeyRegistrationErrorReason.RestorationFailed, error.Reason);
        Assert.Equal("Ctrl+Up", error.Gesture);
        Assert.Equal(5, error.Win32Code);
        HotkeyRegistrationError restoration = Assert.Single(error.RestorationFailures);
        Assert.Equal(HotkeyAction.TogglePower, restoration.Action);
        Assert.Equal("Ctrl+P", restoration.Gesture);
        Assert.Equal(1409, restoration.Win32Code);
        Assert.False(manager.IsEnabled);
        SendAll(manager, backend);
        Assert.Empty(dispatched);
        backend.UnregisterFailure = null;
        backend.RegisterFailure = null;
        Assert.True(manager.TryApply(true, OriginalBindings, out _));
        Assert.True(manager.IsEnabled);
        SendAll(manager, backend);
        Assert.Equal(2, dispatched.Count);
    }

    [Fact]
    public void FailedStagedCleanupRemainsTrackedForLaterDisable()
    {
        FakeHotkeyBackend backend = new();
        List<HotkeyAction> dispatched = [];
        using GlobalHotkeyManager manager = new(nint.Zero, dispatched.Add, backend);
        Assert.True(manager.TryApply(true, OriginalBindings, out _));
        backend.RegisterFailure = (_, _, virtualKey) => virtualKey == 0x59 ? 1409 : 0;
        backend.UnregisterFailure = registrationId => backend.Registrations[registrationId].VirtualKey == 0x4D ? 5 : 0;

        Assert.False(manager.TryApply(true, [new(HotkeyAction.ToggleMute, "Ctrl+M"),
            new(HotkeyAction.ShowWindow, "Ctrl+Y")], out var error));

        Assert.NotNull(error);
        Assert.Equal(HotkeyRegistrationErrorReason.RestorationFailed, error.Reason);
        Assert.Equal(1409, error.Win32Code);
        Assert.Equal("Ctrl+M", Assert.Single(error.RestorationFailures).Gesture);
        Assert.Equal(3, backend.Registrations.Count);
        Assert.False(manager.IsEnabled);
        SendAll(manager, backend);
        Assert.Empty(dispatched);
        backend.UnregisterFailure = null;
        Assert.True(manager.TryApply(false, [], out _));
        Assert.Empty(backend.Registrations);
    }

    [Fact]
    public void DisposeReleasesRegistrationsAndPreventsFurtherConfiguration()
    {
        FakeHotkeyBackend backend = new();
        List<HotkeyAction> dispatched = [];
        GlobalHotkeyManager manager = new(nint.Zero, dispatched.Add, backend);
        Assert.True(manager.TryApply(true, OriginalBindings, out _));
        Dictionary<int, FakeRegistration> originals = new(backend.Registrations);
        IDisposable suspension = manager.Suspend();

        manager.Dispose();
        manager.Dispose();
        suspension.Dispose();

        Assert.False(manager.IsEnabled);
        Assert.Empty(backend.Registrations);
        Assert.Equal(2, backend.UnregisterAttempts.Count);
        foreach ((int registrationId, FakeRegistration registration) in originals)
            Assert.False(Send(manager, registrationId, registration));
        Assert.Empty(dispatched);
        Assert.Throws<ObjectDisposedException>(() => manager.TryApply(true, OriginalBindings, out _));
        Assert.Throws<ObjectDisposedException>(() => manager.Suspend());
    }

    [Fact]
    public void DisposeAttemptsEveryRegistrationReportsFailureAndAllowsCleanupRetry()
    {
        FakeHotkeyBackend backend = new();
        GlobalHotkeyManager manager = new(nint.Zero, _ => { }, backend);
        Assert.True(manager.TryApply(true, OriginalBindings, out _));
        int failedId = backend.Registrations.First().Key;
        backend.UnregisterFailure = registrationId => registrationId == failedId ? 5 : 0;

        HotkeyRegistrationException exception = Assert.Throws<HotkeyRegistrationException>(manager.Dispose);

        Assert.Equal(HotkeyRegistrationErrorReason.UnregistrationFailed, exception.Error.Reason);
        Assert.Equal(5, exception.Error.Win32Code);
        Assert.Equal(2, backend.UnregisterAttempts.Count);
        Assert.Single(backend.Registrations);
        Assert.False(manager.IsEnabled);
        backend.UnregisterFailure = null;
        manager.Dispose();
        Assert.Empty(backend.Registrations);
    }

    private static bool Send(GlobalHotkeyManager manager, int registrationId, FakeRegistration registration)
    {
        bool handled = false;
        manager.ProcessWindowMessage(nint.Zero, GlobalHotkeyManager.HotkeyMessage, registrationId,
            Pack(registration), ref handled);
        return handled;
    }

    private static nint Pack(FakeRegistration registration)
        => (nint)(((long)registration.VirtualKey << 16) | (registration.Modifiers & 0xFu));

    private static void SendAll(GlobalHotkeyManager manager, FakeHotkeyBackend backend)
    {
        foreach ((int registrationId, FakeRegistration registration) in backend.Registrations.ToArray())
            Assert.True(Send(manager, registrationId, registration));
    }

    private static void AssertRegistrations(Dictionary<int, FakeRegistration> expected, FakeHotkeyBackend backend)
    {
        Assert.Equal(expected.Count, backend.Registrations.Count);
        foreach ((int registrationId, FakeRegistration registration) in expected)
            Assert.Equal(registration, backend.Registrations[registrationId]);
    }

    private sealed record FakeRegistration(uint Modifiers, uint VirtualKey);

    private sealed class FakeHotkeyBackend : IHotkeyBackend
    {
        internal Dictionary<int, FakeRegistration> Registrations { get; } = [];
        internal List<int> RegisterAttempts { get; } = [];
        internal List<int> UnregisterAttempts { get; } = [];
        internal List<string> Operations { get; } = [];
        internal Func<int, uint, uint, int>? RegisterFailure { get; set; }
        internal Func<int, int>? UnregisterFailure { get; set; }

        public bool TryRegister(int id, uint modifiers, uint virtualKey, out int win32Code)
        {
            RegisterAttempts.Add(id);
            Operations.Add("register");
            win32Code = RegisterFailure?.Invoke(id, modifiers, virtualKey) ?? 0;
            if (win32Code != 0)
                return false;
            if (Registrations.ContainsKey(id) || Registrations.Values.Any(registration =>
                registration.VirtualKey == virtualKey && registration.Modifiers == modifiers))
            {
                win32Code = 1409;
                return false;
            }
            Registrations.Add(id, new FakeRegistration(modifiers, virtualKey));
            return true;
        }

        public bool TryUnregister(int id, out int win32Code)
        {
            UnregisterAttempts.Add(id);
            Operations.Add("unregister");
            win32Code = UnregisterFailure?.Invoke(id) ?? 0;
            if (win32Code != 0)
                return false;
            if (!Registrations.Remove(id))
            {
                win32Code = 1419;
                return false;
            }
            return true;
        }
    }
}
