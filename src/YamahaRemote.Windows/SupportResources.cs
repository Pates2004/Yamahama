using System.IO;

namespace YamahaRemote.Windows;

public enum SupportGuide
{
    Firmware,
    TouchPanel,
    Reset
}

public static class SupportResources
{
    public const string FirmwareDownloadsAddress = "https://europe.yamaha.com/en/audio/home-audio/products/sound-bar/sr-c30a/support.html";
    public const string YamahaSupportAddress = "https://pl.yamaha.com/pl/support/contacts/index.html";

    public static string ReadGuide(SupportGuide guide)
    {
        string resourceName = guide switch
        {
            SupportGuide.Firmware => $"Yamahama.Guides.Firmware.{Localization.CurrentLanguage}.txt",
            SupportGuide.TouchPanel => $"Yamahama.Guides.TouchPanel.{Localization.CurrentLanguage}.txt",
            SupportGuide.Reset => $"Yamahama.Guides.Reset.{Localization.CurrentLanguage}.txt",
            _ => throw new ArgumentOutOfRangeException(nameof(guide))
        };
        using Stream stream = typeof(SupportResources).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(Localization.Text("Guide.Missing"));
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
