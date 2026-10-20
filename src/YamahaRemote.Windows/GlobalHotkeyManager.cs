using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace YamahaRemote.Windows;

internal interface IHotkeyBackend
{
    bool TryRegister(int id, uint modifiers, uint virtualKey, out int win32Code);
    bool TryUnregister(int id, out int win32Code);
}

internal sealed class HotkeyRegistrationException(HotkeyRegistrationError error)
    : InvalidOperationException($"Hotkey {error.Gesture}: {error.Reason} (Win32 {error.Win32Code}).")
{
    public HotkeyRegistrationError Error { get; } = error;
}

internal sealed class GlobalHotkeyManager : IDisposable
{
    internal const int HotkeyMessage = 0x0312;
    internal const uint NoRepeatModifier = 0x4000;
    private const int MaximumRegistrationId = 0xBFFF;
    private static int _nextRegistrationId;
    private readonly IHotkeyBackend _backend;
    private readonly Action<HotkeyAction> _callback;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<int, Registration> _registrations = [];
    private Dictionary<int, HotkeyBinding> _dispatchBindings = [];
    private HwndSource? _source;
    private bool _disposed;
    private bool _applying;
    private int _suspensionCount;

    internal GlobalHotkeyManager(nint handle, Action<HotkeyAction> callback, IHotkeyBackend? backend = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        if (backend is not null)
        {
            _backend = backend;
            return;
        }

        _source = HwndSource.FromHwnd(handle)
            ?? throw new ArgumentException("The handle must belong to a live WPF window.", nameof(handle));
        _source.Dispatcher.VerifyAccess();
        _backend = new Win32HotkeyBackend(handle);
        _source.AddHook(ProcessWindowMessage);
        _source.Disposed += OnSourceDisposed;
    }

    public bool IsEnabled { get; private set; }

    internal static bool ValidateBindings(IEnumerable<HotkeyBinding> bindings, out HotkeyRegistrationError? error)
        => HotkeyGestureParser.ValidateBindings(bindings, out error);

    internal bool TryApply(bool enabled, IEnumerable<HotkeyBinding> bindings, out HotkeyRegistrationError? error)
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bindings);
        if (_applying)
            throw new InvalidOperationException("A hotkey reconfiguration is already in progress.");

        HotkeyBinding[] requestedBindings = enabled ? bindings.ToArray() : [];
        if (enabled && !ValidateBindings(requestedBindings, out error))
            return false;

        _applying = true;
        try
        {
            return TryReconfigure(enabled, requestedBindings, out error);
        }
        finally
        {
            _applying = false;
        }
    }

    internal IDisposable Suspend()
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _suspensionCount++;
        return new Suspension(this);
    }

    internal nint ProcessWindowMessage(nint handle, int message, nint parameter, nint data, ref bool handled)
    {
        if (message != HotkeyMessage || parameter < 0 || parameter > MaximumRegistrationId
            || !_registrations.TryGetValue((int)parameter, out Registration? registration))
            return nint.Zero;

        handled = true;
        long packedGesture = data.ToInt64();
        uint modifiers = (uint)(packedGesture & 0xFFFF);
        uint virtualKey = (uint)((packedGesture >> 16) & 0xFFFF);
        if (_disposed || !IsEnabled || _applying || _suspensionCount != 0
            || modifiers != (uint)registration.Gesture.Modifiers || virtualKey != registration.Gesture.VirtualKey
            || !_dispatchBindings.TryGetValue(registration.Id, out HotkeyBinding? binding))
            return nint.Zero;

        _callback(binding.Action);
        return nint.Zero;
    }

    public void Dispose()
    {
        VerifyAccess();
        if (_disposed && _registrations.Count == 0)
            return;

        _disposed = true;
        IsEnabled = false;
        _dispatchBindings.Clear();
        List<HotkeyRegistrationError> failures = [];
        foreach (Registration registration in _registrations.Values.ToArray())
        {
            if (!TryRemove(registration, out HotkeyRegistrationError? error))
                failures.Add(error!);
        }
        if (_source is not null)
        {
            _source.Disposed -= OnSourceDisposed;
            _source.RemoveHook(ProcessWindowMessage);
            _source = null;
        }
        if (failures.Count != 0)
            throw new HotkeyRegistrationException(failures[0] with { RestorationFailures = failures.ToArray() });
    }

    private bool TryReconfigure(bool enabled, IReadOnlyList<HotkeyBinding> requestedBindings,
        out HotkeyRegistrationError? error)
    {
        List<Registration> staged = [];
        Dictionary<int, HotkeyBinding> nextDispatch = [];
        foreach (HotkeyBinding binding in requestedBindings)
        {
            if (string.IsNullOrWhiteSpace(binding.Gesture))
                continue;
            HotkeyGestureParser.TryParse(binding.Gesture, out HotkeyGesture gesture);
            Registration? registration = _registrations.Values.FirstOrDefault(existing => existing.Gesture == gesture);
            if (registration is null)
            {
                registration = new Registration(NextRegistrationId(), gesture,
                    binding with { Gesture = gesture.Format() });
                if (!TryAdd(registration, out error))
                {
                    List<HotkeyRegistrationError> failures = RemoveStaged(staged);
                    if (failures.Count != 0)
                        error = RestorationFailure(error!, failures);
                    return false;
                }
                staged.Add(registration);
            }
            nextDispatch.Add(registration.Id, binding with { Gesture = gesture.Format() });
        }

        List<Registration> removed = [];
        foreach (Registration registration in _registrations.Values.ToArray())
        {
            if (nextDispatch.ContainsKey(registration.Id))
                continue;
            if (!TryRemove(registration, out error))
            {
                List<HotkeyRegistrationError> failures = RemoveStaged(staged);
                foreach (Registration previous in removed)
                {
                    if (!TryAdd(previous, out HotkeyRegistrationError? restorationError))
                        failures.Add(restorationError!);
                }
                if (failures.Count != 0)
                    error = RestorationFailure(error!, failures);
                return false;
            }
            removed.Add(registration);
        }

        foreach ((int registrationId, HotkeyBinding binding) in nextDispatch)
            _registrations[registrationId] = _registrations[registrationId] with { Binding = binding };
        _dispatchBindings = nextDispatch;
        IsEnabled = enabled;
        error = null;
        return true;
    }

    private bool TryAdd(Registration registration, out HotkeyRegistrationError? error)
    {
        if (!_backend.TryRegister(registration.Id, (uint)registration.Gesture.Modifiers | NoRepeatModifier,
            registration.Gesture.VirtualKey, out int win32Code))
        {
            error = new HotkeyRegistrationError(registration.Binding.Action, registration.Gesture.Format(),
                HotkeyRegistrationErrorReason.RegistrationFailed, win32Code);
            return false;
        }
        _registrations.Add(registration.Id, registration);
        error = null;
        return true;
    }

    private bool TryRemove(Registration registration, out HotkeyRegistrationError? error)
    {
        if (!_backend.TryUnregister(registration.Id, out int win32Code))
        {
            error = new HotkeyRegistrationError(registration.Binding.Action, registration.Gesture.Format(),
                HotkeyRegistrationErrorReason.UnregistrationFailed, win32Code);
            return false;
        }
        _registrations.Remove(registration.Id);
        error = null;
        return true;
    }

    private List<HotkeyRegistrationError> RemoveStaged(IEnumerable<Registration> staged)
    {
        List<HotkeyRegistrationError> failures = [];
        foreach (Registration registration in staged)
        {
            if (!TryRemove(registration, out HotkeyRegistrationError? error))
                failures.Add(error!);
        }
        return failures;
    }

    private HotkeyRegistrationError RestorationFailure(HotkeyRegistrationError original,
        IReadOnlyList<HotkeyRegistrationError> failures)
    {
        IsEnabled = false;
        _dispatchBindings.Clear();
        return original with
        {
            Reason = HotkeyRegistrationErrorReason.RestorationFailed,
            RestorationFailures = failures.ToArray()
        };
    }

    private int NextRegistrationId()
    {
        for (int attempt = 0; attempt < MaximumRegistrationId; attempt++)
        {
            int candidate = (int)((uint)Interlocked.Increment(ref _nextRegistrationId) % MaximumRegistrationId) + 1;
            if (!_registrations.ContainsKey(candidate))
                return candidate;
        }
        throw new InvalidOperationException("No application hotkey identifiers remain available.");
    }

    private void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Hotkeys must be managed on the window's owning thread.");
    }

    private void OnSourceDisposed(object? sender, EventArgs eventArgs)
    {
        IsEnabled = false;
        _disposed = true;
        _registrations.Clear();
        _dispatchBindings.Clear();
        if (_source is not null)
            _source.Disposed -= OnSourceDisposed;
        _source = null;
    }

    private sealed record Registration(int Id, HotkeyGesture Gesture, HotkeyBinding Binding);

    private sealed class Suspension(GlobalHotkeyManager manager) : IDisposable
    {
        private GlobalHotkeyManager? _manager = manager;

        public void Dispose()
        {
            if (_manager is null)
                return;
            _manager.VerifyAccess();
            _manager._suspensionCount--;
            _manager = null;
        }
    }

    private sealed class Win32HotkeyBackend(nint handle) : IHotkeyBackend
    {
        public bool TryRegister(int id, uint modifiers, uint virtualKey, out int win32Code)
        {
            bool success = RegisterHotKey(handle, id, modifiers, virtualKey);
            win32Code = success ? 0 : Marshal.GetLastWin32Error();
            return success;
        }

        public bool TryUnregister(int id, out int win32Code)
        {
            bool success = UnregisterHotKey(handle, id);
            win32Code = success ? 0 : Marshal.GetLastWin32Error();
            if (!success && win32Code is 1400 or 1419)
            {
                win32Code = 0;
                return true;
            }
            return success;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(nint handle, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(nint handle, int id);
    }
}
