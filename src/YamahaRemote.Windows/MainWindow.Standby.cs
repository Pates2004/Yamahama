using System.Windows;
using System.Windows.Automation;

namespace YamahaRemote.Windows;

public partial class MainWindow
{
    private bool IsSoundbarInStandby => Connected && _controller.State is { Power: false };

    private bool RejectStandbyEdit()
    {
        if (!IsSoundbarInStandby)
            return false;
        _manualSelectionDrafts.Clear();
        SynchronizeSettingControls();
        SetStatus(Localization.Text("State.PowerOffEditBlocked"), important: true);
        return true;
    }

    private void UpdateStandbyAccessibility()
    {
        string status = IsSoundbarInStandby ? Localization.Text("State.PowerOffEditBlocked") : string.Empty;
        foreach (UIElement control in new UIElement[]
        {
            InputChoice, ModeChoice, BrightnessChoice, MuteToggle, ClearVoiceToggle, BassToggle,
            ApplyInputButton, ApplyModeButton, ApplyBrightnessButton, IncreaseVolumeButton,
            DecreaseVolumeButton, IncreaseSubwooferButton, DecreaseSubwooferButton,
            SaveSoundProfileButton, ApplySoundProfileButton
        })
            AutomationProperties.SetItemStatus(control, status);
    }
}
