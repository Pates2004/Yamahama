using System.Globalization;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace YamahaRemote.Windows;

[JsonConverter(typeof(HotkeyActionJsonConverter))]
internal enum HotkeyAction
{
    TogglePower,
    VolumeUp,
    VolumeDown,
    ToggleMute,
    SubwooferUp,
    SubwooferDown,
    InputTv,
    InputOptical,
    InputBluetooth,
    InputAnalog,
    ModeStereo,
    ModeStandard,
    ModeMovie3D,
    ModeGame,
    ToggleClearVoice,
    ToggleBassExtension,
    BrightnessBright,
    BrightnessDim,
    BrightnessOff,
    ShowWindow
}

internal sealed class HotkeyActionJsonConverter : JsonStringEnumConverter<HotkeyAction>
{
    public HotkeyActionJsonConverter() : base(allowIntegerValues: false)
    {
    }
}

internal sealed record HotkeyBinding([property: JsonRequired] HotkeyAction Action, [property: JsonRequired] string Gesture);

internal static class HotkeyDefaults
{
    internal static IReadOnlyList<HotkeyBinding> Create() => Enum.GetValues<HotkeyAction>()
        .Select(action => new HotkeyBinding(action, action switch
        {
            HotkeyAction.TogglePower => "Ctrl+Alt+Shift+P",
            HotkeyAction.VolumeUp => "Ctrl+Alt+Shift+Up",
            HotkeyAction.VolumeDown => "Ctrl+Alt+Shift+Down",
            HotkeyAction.ToggleMute => "Ctrl+Alt+Shift+M",
            HotkeyAction.SubwooferUp => "Ctrl+Alt+Shift+Right",
            HotkeyAction.SubwooferDown => "Ctrl+Alt+Shift+Left",
            HotkeyAction.ShowWindow => "Ctrl+Alt+Shift+Y",
            _ => string.Empty
        }))
        .ToArray();
}

internal enum HotkeyRegistrationErrorReason
{
    InvalidAction,
    InvalidGesture,
    ReservedGesture,
    DuplicateAction,
    DuplicateGesture,
    RegistrationFailed,
    UnregistrationFailed,
    RestorationFailed
}

internal sealed record HotkeyRegistrationError(
    HotkeyAction Action,
    string Gesture,
    HotkeyRegistrationErrorReason Reason,
    int Win32Code = 0)
{
    public HotkeyAction? ConflictingAction { get; init; }
    public IReadOnlyList<HotkeyRegistrationError> RestorationFailures { get; init; } = [];
}

internal readonly record struct HotkeyGesture(Key Key, ModifierKeys Modifiers)
{
    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public string Format()
    {
        List<string> parts = [];
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)Key - (int)Key.D0).ToString(CultureInfo.InvariantCulture),
            Key.Return => "Enter",
            Key.Prior => "PageUp",
            Key.Next => "PageDown",
            Key.Capital => "CapsLock",
            Key.Back => "Backspace",
            Key.Snapshot => "PrintScreen",
            _ => Key.ToString()
        });
        return string.Join("+", parts);
    }

    public override string ToString() => Format();
}

internal static class HotkeyGestureParser
{
    internal const int MaximumGestureLength = 128;
    private const ModifierKeys SupportedModifiers = ModifierKeys.Control | ModifierKeys.Alt
        | ModifierKeys.Shift | ModifierKeys.Windows;

    internal static bool TryParse(string? value, out HotkeyGesture gesture)
        => TryParse(value, out gesture, out _);

    internal static bool TryParse(string? value, out HotkeyGesture gesture,
        out HotkeyRegistrationErrorReason reason)
    {
        gesture = default;
        reason = HotkeyRegistrationErrorReason.InvalidGesture;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumGestureLength || value.Any(char.IsControl))
            return false;

        string[] parts = value.Split('+', StringSplitOptions.TrimEntries);
        ModifierKeys modifiers = ModifierKeys.None;
        for (int index = 0; index < parts.Length - 1; index++)
        {
            ModifierKeys modifier = parts[index].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => ModifierKeys.Control,
                "ALT" => ModifierKeys.Alt,
                "SHIFT" => ModifierKeys.Shift,
                "WIN" or "WINDOWS" => ModifierKeys.Windows,
                _ => ModifierKeys.None
            };
            if (modifier == ModifierKeys.None || (modifiers & modifier) != 0)
                return false;
            modifiers |= modifier;
        }

        string keyName = parts[^1].ToUpperInvariant() switch
        {
            "ESC" => nameof(Key.Escape),
            "PGUP" => nameof(Key.PageUp),
            "PGDN" => nameof(Key.PageDown),
            "DEL" => nameof(Key.Delete),
            "INS" => nameof(Key.Insert),
            "BACKSPACE" => nameof(Key.Back),
            _ => parts[^1]
        };
        if (keyName.Length == 1 && keyName[0] is >= '0' and <= '9')
            keyName = "D" + keyName;
        if (keyName.Length == 0 || !char.IsLetter(keyName[0]) || keyName.Any(character => !char.IsLetterOrDigit(character))
            || !Enum.TryParse(keyName, ignoreCase: true, out Key key) || !Enum.IsDefined(key))
            return false;

        return TryFromKey(key, modifiers, out gesture, out reason);
    }

    internal static bool TryFromKey(Key key, ModifierKeys modifiers, out HotkeyGesture gesture)
        => TryFromKey(key, modifiers, out gesture, out _);

    internal static bool TryFromKey(KeyEventArgs keyEvent, ModifierKeys modifiers, out HotkeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(keyEvent);
        Key key = keyEvent.Key switch
        {
            Key.System => keyEvent.SystemKey,
            Key.ImeProcessed => keyEvent.ImeProcessedKey,
            Key.DeadCharProcessed => keyEvent.DeadCharProcessedKey,
            _ => keyEvent.Key
        };
        return TryFromKey(key, modifiers, out gesture);
    }

    internal static bool TryFromKey(Key key, ModifierKeys modifiers, out HotkeyGesture gesture,
        out HotkeyRegistrationErrorReason reason)
    {
        gesture = default;
        reason = HotkeyRegistrationErrorReason.InvalidGesture;
        if ((modifiers & ~SupportedModifiers) != 0 || !Enum.IsDefined(key)
            || key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed
                or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return false;

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey is <= 0 or > 254)
            return false;

        if (key == Key.F12 || key == Key.Delete && modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Alt)
            || key == Key.Tab && modifiers == ModifierKeys.Alt
            || key == Key.Escape && modifiers is ModifierKeys.Alt or ModifierKeys.Control
            || key == Key.Escape && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)
            || key == Key.F4 && modifiers == ModifierKeys.Alt
            || key == Key.L && modifiers.HasFlag(ModifierKeys.Windows))
        {
            reason = HotkeyRegistrationErrorReason.ReservedGesture;
            return false;
        }

        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0
            && !(modifiers == ModifierKeys.None && key is >= Key.F13 and <= Key.F24))
            return false;

        gesture = new HotkeyGesture(key, modifiers);
        return true;
    }

    internal static bool ValidateBindings(IEnumerable<HotkeyBinding> bindings, out HotkeyRegistrationError? error)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        error = null;
        HashSet<HotkeyAction> actions = [];
        Dictionary<HotkeyGesture, HotkeyAction> gestures = [];
        foreach (HotkeyBinding binding in bindings)
        {
            if (binding is null || !Enum.IsDefined(binding.Action))
            {
                error = new HotkeyRegistrationError(binding?.Action ?? default, binding?.Gesture ?? string.Empty,
                    HotkeyRegistrationErrorReason.InvalidAction);
                return false;
            }
            if (!actions.Add(binding.Action))
            {
                error = new HotkeyRegistrationError(binding.Action, binding.Gesture ?? string.Empty,
                    HotkeyRegistrationErrorReason.DuplicateAction);
                return false;
            }
            if (binding.Gesture is null || binding.Gesture.Length > MaximumGestureLength
                || binding.Gesture.Any(char.IsControl))
            {
                error = new HotkeyRegistrationError(binding.Action, binding.Gesture ?? string.Empty,
                    HotkeyRegistrationErrorReason.InvalidGesture);
                return false;
            }
            if (string.IsNullOrWhiteSpace(binding.Gesture))
                continue;
            if (!TryParse(binding.Gesture, out HotkeyGesture gesture, out HotkeyRegistrationErrorReason reason))
            {
                error = new HotkeyRegistrationError(binding.Action, binding.Gesture, reason);
                return false;
            }
            if (gestures.TryGetValue(gesture, out HotkeyAction conflictingAction))
            {
                error = new HotkeyRegistrationError(binding.Action, gesture.Format(),
                    HotkeyRegistrationErrorReason.DuplicateGesture) { ConflictingAction = conflictingAction };
                return false;
            }
            gestures.Add(gesture, binding.Action);
        }
        return true;
    }
}
