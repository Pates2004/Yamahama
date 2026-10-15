using System.Globalization;
using YamahaRemote.Core;

namespace YamahaRemote.Windows;

internal sealed record TrayPresentation(
    string Status, string Tooltip, string VolumeLabel, string SubwooferLabel, string InputLabel)
{
    internal const int TooltipLimit = 127;

    internal static TrayPresentation Create(bool connected, SoundbarState? state, bool busy,
        Func<string, object[], string> text, CultureInfo culture)
    {
        state = connected ? state : null;
        string connection = Text(!connected ? "Tray.NotConnected"
            : state is null ? "Tray.StateUnknown" : "Tray.Connected");
        string status = busy ? connection + "; " + Text("Tray.Busy") : connection;
        string unknown = Text("Readout.Unknown");
        string power = state is null ? unknown : Text(state.Power ? "State.On" : "Feedback.PowerOff");
        string volume = state is { Volume: >= 0 and <= 50 } ? state.Volume.ToString(culture) : Text("Readout.UnknownFeminine");
        string subwoofer = state is { Subwoofer: >= -4 and <= 4 } ? state.Subwoofer.ToString(culture) : Text("Readout.Unknown");
        string input = state is null ? unknown : Text(state.Input switch
        {
            SoundbarInput.Tv => "Input.Tv",
            SoundbarInput.Optical => "Input.Optical",
            SoundbarInput.Bluetooth => "Input.Bluetooth",
            SoundbarInput.Analog => "Input.Analog",
            _ => "Input.Unknown"
        });
        string volumeLabel = Group("Tray.Volume", volume);
        string subwooferLabel = Group("Tray.Subwoofer", subwoofer);
        string inputLabel = Group("Tray.Inputs", input);
        string tooltip = Text("Tray.Summary", "SR-C30A: " + connection,
            power, volume, subwoofer);
        if (busy)
            tooltip = AppendIfFits(tooltip, Text("Tray.Busy"));
        if (state is not null)
            tooltip = AppendIfFits(tooltip, inputLabel);
        return new(status, LimitTooltip(tooltip), volumeLabel, subwooferLabel, inputLabel);

        string Text(string key, params object[] arguments) => text(key, arguments);

        string Group(string key, string value) => Text("Tray.GroupValue",
            Text(key).Replace("_", string.Empty, StringComparison.Ordinal), value);
    }

    private static string AppendIfFits(string tooltip, string detail) =>
        tooltip.Length + 2 + detail.Length <= TooltipLimit ? tooltip + "; " + detail : tooltip;

    internal static string LimitTooltip(string text)
    {
        if (text.Length <= TooltipLimit)
            return text;
        int length = TooltipLimit - 1;
        if (char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length]))
            length--;
        return text[..length] + "…";
    }
}
