using System.Text.Json;
using System.Windows.Input;
using Xunit;

namespace YamahaRemote.Windows.Tests;

public sealed class GlobalHotkeyGestureTests
{
    [Theory]
    [InlineData(" shift + alt + control + p ", "Ctrl+Alt+Shift+P", Key.P, 7, 0x50)]
    [InlineData("Control+Up", "Ctrl+Up", Key.Up, 2, 0x26)]
    [InlineData("Ctrl+Alt+Down", "Ctrl+Alt+Down", Key.Down, 3, 0x28)]
    [InlineData("Ctrl+Shift+Left", "Ctrl+Shift+Left", Key.Left, 6, 0x25)]
    [InlineData("Alt+Right", "Alt+Right", Key.Right, 1, 0x27)]
    [InlineData("windows+ctrl+F2", "Ctrl+Win+F2", Key.F2, 10, 0x71)]
    [InlineData("Ctrl+enter", "Ctrl+Enter", Key.Return, 2, 0x0D)]
    [InlineData("ctrl+pgup", "Ctrl+PageUp", Key.Prior, 2, 0x21)]
    [InlineData("ctrl+pgdn", "Ctrl+PageDown", Key.Next, 2, 0x22)]
    [InlineData("ctrl+backspace", "Ctrl+Backspace", Key.Back, 2, 0x08)]
    [InlineData("ctrl+1", "Ctrl+1", Key.D1, 2, 0x31)]
    [InlineData("Ctrl+D0", "Ctrl+0", Key.D0, 2, 0x30)]
    [InlineData("Ctrl+NumPad1", "Ctrl+NumPad1", Key.NumPad1, 2, 0x61)]
    [InlineData("F13", "F13", Key.F13, 0, 0x7C)]
    [InlineData("F24", "F24", Key.F24, 0, 0x87)]
    public void ParserNormalizesInvariantGesturesAndConvertsVirtualKeys(string text, string canonical,
        Key expectedKey, int expectedModifiers, int virtualKey)
    {
        Assert.True(HotkeyGestureParser.TryParse(text, out HotkeyGesture gesture));
        Assert.Equal(expectedKey, gesture.Key);
        Assert.Equal((ModifierKeys)expectedModifiers, gesture.Modifiers);
        Assert.Equal((uint)virtualKey, gesture.VirtualKey);
        Assert.Equal(canonical, gesture.Format());
        Assert.Equal(canonical, gesture.ToString());
        Assert.True(HotkeyGestureParser.TryParse(gesture.Format(), out HotkeyGesture roundTrip));
        Assert.Equal(gesture, roundTrip);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("P")]
    [InlineData("F1")]
    [InlineData("Up")]
    [InlineData("Escape")]
    [InlineData("Shift+A")]
    [InlineData("Shift+Tab")]
    [InlineData("Shift+F13")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+LeftCtrl")]
    [InlineData("Ctrl+RWin")]
    [InlineData("Ctrl+None")]
    [InlineData("Ctrl+System")]
    [InlineData("Ctrl+ImeProcessed")]
    [InlineData("Ctrl+DeadCharProcessed")]
    [InlineData("Ctrl+Ctrl+P")]
    [InlineData("Ctrl+Control+P")]
    [InlineData("Ctrl++P")]
    [InlineData("Ctrl+P+")]
    [InlineData("+P")]
    [InlineData("Ctrl+P+Alt")]
    [InlineData("Super+P")]
    [InlineData("Ctrl+999")]
    [InlineData("Ctrl+-1")]
    [InlineData("Ctrl+A, B")]
    [InlineData("Ctrl+P\t")]
    [InlineData("Ctrl+\nP")]
    public void MalformedOrUnsafeGesturesAreRejected(string? text)
    {
        Assert.False(HotkeyGestureParser.TryParse(text, out _, out HotkeyRegistrationErrorReason reason));
        Assert.Equal(HotkeyRegistrationErrorReason.InvalidGesture, reason);
    }

    [Theory]
    [InlineData("Ctrl+F12")]
    [InlineData("Ctrl+Alt+Shift+F12")]
    [InlineData("Ctrl+Alt+Delete")]
    [InlineData("Ctrl+Alt+Shift+Delete")]
    [InlineData("Alt+Tab")]
    [InlineData("Alt+Escape")]
    [InlineData("Ctrl+Escape")]
    [InlineData("Ctrl+Shift+Escape")]
    [InlineData("Alt+F4")]
    [InlineData("Win+L")]
    [InlineData("Ctrl+Win+L")]
    public void ReservedGesturesHaveADistinctValidationReason(string text)
    {
        Assert.False(HotkeyGestureParser.TryParse(text, out _, out HotkeyRegistrationErrorReason reason));
        Assert.Equal(HotkeyRegistrationErrorReason.ReservedGesture, reason);
    }

    [Theory]
    [InlineData(Key.P, 7, "Ctrl+Alt+Shift+P")]
    [InlineData(Key.Up, 2, "Ctrl+Up")]
    [InlineData(Key.Return, 1, "Alt+Enter")]
    [InlineData(Key.F13, 0, "F13")]
    [InlineData(Key.F24, 0, "F24")]
    [InlineData(Key.OemPlus, 2, "")]
    public void CaptureAndTextParserUseTheSameKeyConversion(Key key, int modifiers, string expected)
    {
        Assert.True(HotkeyGestureParser.TryFromKey(key, (ModifierKeys)modifiers, out HotkeyGesture captured));
        Assert.Equal((uint)KeyInterop.VirtualKeyFromKey(key), captured.VirtualKey);
        if (expected.Length != 0)
            Assert.Equal(expected, captured.Format());
        Assert.True(HotkeyGestureParser.TryParse(captured.Format(), out HotkeyGesture parsed));
        Assert.Equal(captured, parsed);
    }

    [Theory]
    [InlineData(Key.LeftCtrl, 2)]
    [InlineData(Key.RightAlt, 1)]
    [InlineData(Key.LeftShift, 4)]
    [InlineData(Key.P, 16)]
    [InlineData(Key.P, 4)]
    [InlineData(Key.Tab, 4)]
    [InlineData(Key.Escape, 0)]
    [InlineData(Key.F13, 4)]
    [InlineData((Key)9999, 2)]
    public void CaptureRejectsModifiersAloneInvalidKeysAndUnsafeTyping(Key key, int modifiers)
        => Assert.False(HotkeyGestureParser.TryFromKey(key, (ModifierKeys)modifiers, out _));

    [Fact]
    public void DefaultsContainAllActionsExactlyOnceAndEveryBoundGestureIsCanonical()
    {
        IReadOnlyList<HotkeyBinding> defaults = HotkeyDefaults.Create();

        Assert.Equal(Enum.GetValues<HotkeyAction>(), defaults.Select(binding => binding.Action));
        Assert.Equal(7, defaults.Count(binding => binding.Gesture.Length != 0));
        Assert.True(GlobalHotkeyManager.ValidateBindings(defaults, out HotkeyRegistrationError? error));
        Assert.Null(error);
        foreach (HotkeyBinding binding in defaults.Where(binding => binding.Gesture.Length != 0))
        {
            Assert.True(HotkeyGestureParser.TryParse(binding.Gesture, out HotkeyGesture gesture));
            Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, gesture.Modifiers);
            Assert.Equal(binding.Gesture, gesture.Format());
        }
        Assert.Equal("Ctrl+Alt+Shift+Y", defaults.Single(binding => binding.Action == HotkeyAction.ShowWindow).Gesture);
    }

    [Fact]
    public void ValidationAcceptsSparseAndUnassignedBindings()
    {
        HotkeyBinding[] bindings = [new(HotkeyAction.TogglePower, ""), new(HotkeyAction.ShowWindow, "   ")];

        Assert.True(GlobalHotkeyManager.ValidateBindings(bindings, out HotkeyRegistrationError? error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\t")]
    [InlineData("\n\r")]
    [InlineData(" \0 ")]
    [InlineData("Ctrl+P\n")]
    public void ValidationRejectsNullAndControlCharactersEvenForUnassignedBindings(string? text)
    {
        Assert.False(GlobalHotkeyManager.ValidateBindings([new(HotkeyAction.TogglePower, text!)], out var error));

        Assert.NotNull(error);
        Assert.Equal(HotkeyAction.TogglePower, error.Action);
        Assert.Equal(HotkeyRegistrationErrorReason.InvalidGesture, error.Reason);
    }

    [Fact]
    public void ValidationRejectsOversizedGesturesIncludingWhitespace()
    {
        foreach (string text in new[] { new string(' ', 129), "Ctrl+" + new string('P', 124) })
        {
            Assert.False(GlobalHotkeyManager.ValidateBindings([new(HotkeyAction.VolumeUp, text)], out var error));
            Assert.NotNull(error);
            Assert.Equal(HotkeyRegistrationErrorReason.InvalidGesture, error.Reason);
            Assert.False(HotkeyGestureParser.TryParse(text, out _));
        }
        Assert.True(GlobalHotkeyManager.ValidateBindings([new(HotkeyAction.VolumeUp, new string(' ', 128))], out _));
    }

    [Fact]
    public void ValidationReportsBothActionsSharingANormalizedGesture()
    {
        HotkeyBinding[] bindings = [new(HotkeyAction.TogglePower, "Ctrl+Alt+P"),
            new(HotkeyAction.ToggleMute, " alt + control + p ")];

        Assert.False(GlobalHotkeyManager.ValidateBindings(bindings, out var error));

        Assert.NotNull(error);
        Assert.Equal(HotkeyRegistrationErrorReason.DuplicateGesture, error.Reason);
        Assert.Equal(HotkeyAction.ToggleMute, error.Action);
        Assert.Equal(HotkeyAction.TogglePower, error.ConflictingAction);
        Assert.Equal("Ctrl+Alt+P", error.Gesture);
    }

    [Fact]
    public void ValidationRejectsRepeatedActionsEvenWhenUnassigned()
    {
        Assert.False(GlobalHotkeyManager.ValidateBindings(
            [new(HotkeyAction.TogglePower, ""), new(HotkeyAction.TogglePower, "Ctrl+P")], out var error));

        Assert.NotNull(error);
        Assert.Equal(HotkeyRegistrationErrorReason.DuplicateAction, error.Reason);
    }

    [Fact]
    public void ValidationRejectsUnknownActionsAndNullEntries()
    {
        foreach (HotkeyBinding[] bindings in new[] { new[] { new HotkeyBinding((HotkeyAction)999, "Ctrl+P") },
            new HotkeyBinding[] { null! } })
        {
            Assert.False(GlobalHotkeyManager.ValidateBindings(bindings, out var error));
            Assert.NotNull(error);
            Assert.Equal(HotkeyRegistrationErrorReason.InvalidAction, error.Reason);
        }
    }

    [Fact]
    public void ActionJsonUsesNamesAndRejectsNumericAndUnknownValues()
    {
        Assert.Equal("\"TogglePower\"", JsonSerializer.Serialize(HotkeyAction.TogglePower));
        Assert.Equal(HotkeyAction.ShowWindow, JsonSerializer.Deserialize<HotkeyAction>("\"ShowWindow\""));
        foreach (string json in new[] { "0", "999", "\"999\"", "\"MissingAction\"" })
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<HotkeyAction>(json));
    }
}
