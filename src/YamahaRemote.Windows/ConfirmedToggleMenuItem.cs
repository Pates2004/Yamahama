using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace YamahaRemote.Windows;

internal sealed class ConfirmedToggleMenuItem : MenuItem
{
    internal bool? ConfirmedValue { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new ConfirmedToggleMenuItemPeer(this);

    private sealed class ConfirmedToggleMenuItemPeer(ConfirmedToggleMenuItem owner)
        : MenuItemAutomationPeer(owner), IToggleProvider
    {
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Toggle ? this : base.GetPattern(patternInterface);

        ToggleState IToggleProvider.ToggleState => owner.ConfirmedValue switch
        {
            true => ToggleState.On,
            false => ToggleState.Off,
            null => ToggleState.Indeterminate
        };

        void IToggleProvider.Toggle()
        {
            if (!owner.IsEnabled)
                throw new ElementNotEnabledException();
            owner.RaiseEvent(new RoutedEventArgs(ClickEvent));
        }
    }
}
