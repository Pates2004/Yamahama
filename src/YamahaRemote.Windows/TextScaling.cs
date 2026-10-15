using System.Globalization;
using System.Windows;

namespace YamahaRemote.Windows;

internal static class TextScaling
{
    internal static TextScaleOption[] Choices(int currentScale) =>
        new[] { 100, 125, 150, 175, 200, currentScale }.Distinct().Order()
            .Select(value => new TextScaleOption(value)).ToArray();

    internal static void Apply(Window window, int percentage)
    {
        if (percentage is < 100 or > 200)
            throw new ArgumentOutOfRangeException(nameof(percentage));
        window.FontSize = SystemFonts.MessageFontSize * percentage / 100.0;
    }
}

internal sealed record TextScaleOption(int Value)
{
    public string DisplayName => Value.ToString(CultureInfo.InvariantCulture) + "%";
    public override string ToString() => DisplayName;
}
