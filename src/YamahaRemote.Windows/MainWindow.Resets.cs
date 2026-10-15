using System.Windows;

namespace YamahaRemote.Windows;

public partial class MainWindow
{
    private void OnResetSound(object sender, RoutedEventArgs eventArgs)
    {
        OpenHelpWindow(ResetGuideReadout);
        SetStatus(Localization.Text("Reset.SoundUnavailable"));
    }

    private void OnFactoryResetGuide(object sender, RoutedEventArgs eventArgs)
    {
        OpenHelpWindow(ResetGuideReadout);
        SetStatus(Localization.Text("Reset.FactoryOpened"));
    }
}
