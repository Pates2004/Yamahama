using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace YamahaRemote.Windows;

internal sealed class ConfirmedToggleMenuItem : MenuItem
{
    private bool? _confirmedValue;

    static ConfirmedToggleMenuItem()
    {
        IsCheckableProperty.OverrideMetadata(typeof(ConfirmedToggleMenuItem), new FrameworkPropertyMetadata(true));
        IsCheckedProperty.OverrideMetadata(typeof(ConfirmedToggleMenuItem), new FrameworkPropertyMetadata(false,
            null, (owner, _) => ((ConfirmedToggleMenuItem)owner).ConfirmedValue == true));
    }

    internal bool? ConfirmedValue
    {
        get => _confirmedValue;
        set
        {
            if (_confirmedValue == value)
                return;
            ToggleState previous = GetToggleState(_confirmedValue);
            _confirmedValue = value;
            SetCurrentValue(IsCheckedProperty, value == true);
            CoerceValue(IsCheckedProperty);
            UIElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(
                TogglePatternIdentifiers.ToggleStateProperty, previous, GetToggleState(value));
        }
    }

    private static ToggleState GetToggleState(bool? value) => value switch
    {
        true => ToggleState.On,
        false => ToggleState.Off,
        null => ToggleState.Indeterminate
    };

    protected override AutomationPeer OnCreateAutomationPeer() => new ConfirmedToggleMenuItemPeer(this);

    private sealed class ConfirmedToggleMenuItemPeer(ConfirmedToggleMenuItem owner)
        : MenuItemAutomationPeer(owner), IToggleProvider
    {
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Toggle ? this : base.GetPattern(patternInterface);

        ToggleState IToggleProvider.ToggleState => GetToggleState(owner.ConfirmedValue);

        void IToggleProvider.Toggle()
        {
            if (!owner.IsEnabled)
                throw new ElementNotEnabledException();
            owner.RaiseEvent(new RoutedEventArgs(ClickEvent));
        }
    }
}
