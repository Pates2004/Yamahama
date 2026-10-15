using YamahaRemote.Core;

namespace YamahaRemote.Windows;

internal static class SoundbarFeedback
{
    internal static ControlRow[] ChangedRows(SoundbarState previous, SoundbarState current)
    {
        List<ControlRow> changed = [];
        if (previous.Power != current.Power) changed.Add(ControlRow.Power);
        if (previous.Input != current.Input) changed.Add(ControlRow.Input);
        if (previous.Volume != current.Volume) changed.Add(ControlRow.Volume);
        if (previous.Muted != current.Muted) changed.Add(ControlRow.Mute);
        if (previous.Subwoofer != current.Subwoofer) changed.Add(ControlRow.Subwoofer);
        if (previous.Mode != current.Mode) changed.Add(ControlRow.SoundMode);
        if (previous.ClearVoice != current.ClearVoice) changed.Add(ControlRow.ClearVoice);
        if (previous.BassExtension != current.BassExtension) changed.Add(ControlRow.BassExtension);
        if (previous.Brightness != current.Brightness) changed.Add(ControlRow.IndicatorBrightness);
        return changed.ToArray();
    }
}
