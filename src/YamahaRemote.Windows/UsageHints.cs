using System.Windows;
using System.Windows.Automation;

namespace YamahaRemote.Windows;

public static class UsageHints
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(UsageHints),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits, OnHintChanged));

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(UsageHints), new PropertyMetadata(string.Empty, OnHintChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);
    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

    private static void OnHintChanged(DependencyObject element, DependencyPropertyChangedEventArgs eventArgs) =>
        AutomationProperties.SetHelpText(element, GetEnabled(element) ? GetText(element) : string.Empty);
}
