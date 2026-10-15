using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;

namespace YamahaRemote.Windows;

public sealed class ConfirmedSlider : Slider
{
    private bool _synchronizing;
    private bool _isAdjustmentBlocked;
    internal bool IsAdjustmentBlocked
    {
        get => _isAdjustmentBlocked;
        set
        {
            if (_isAdjustmentBlocked == value)
                return;
            bool previous = _isAdjustmentBlocked || !IsEnabled;
            _isAdjustmentBlocked = value;
            UIElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(
                RangeValuePatternIdentifiers.IsReadOnlyProperty, previous, value || !IsEnabled);
        }
    }
    internal double ConfirmedValue { get; private set; }
    public event RoutedPropertyChangedEventHandler<double>? ValueRequested;

    internal void ConfirmValue(double value, bool preserveRequest)
    {
        double previous = ConfirmedValue;
        ConfirmedValue = value;
        if (!preserveRequest)
        {
            _synchronizing = true;
            try { Value = value; }
            finally { _synchronizing = false; }
        }
        if (previous != value)
        {
            RaiseEvent(new RoutedPropertyChangedEventArgs<double>(previous, value, ValueChangedEvent));
            UIElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(
                RangeValuePatternIdentifiers.ValueProperty, previous, value);
        }
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        if (!_synchronizing)
            ValueRequested?.Invoke(this, new RoutedPropertyChangedEventArgs<double>(oldValue, newValue));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs eventArgs)
    {
        if (IsEnabled && IsAdjustmentBlocked && eventArgs.Key is
            Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            ValueRequested?.Invoke(this, new RoutedPropertyChangedEventArgs<double>(ConfirmedValue, ConfirmedValue));
            eventArgs.Handled = true;
        }
        base.OnPreviewKeyDown(eventArgs);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs eventArgs)
    {
        if (IsEnabled && IsAdjustmentBlocked)
        {
            Focus();
            ValueRequested?.Invoke(this, new RoutedPropertyChangedEventArgs<double>(ConfirmedValue, ConfirmedValue));
            eventArgs.Handled = true;
        }
        base.OnPreviewMouseLeftButtonDown(eventArgs);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ConfirmedSliderPeer(this);

    private sealed class ConfirmedSliderPeer(ConfirmedSlider owner) : SliderAutomationPeer(owner), IRangeValueProvider
    {
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);

        bool IRangeValueProvider.IsReadOnly => owner.IsAdjustmentBlocked || !owner.IsEnabled;
        double IRangeValueProvider.Value => owner.ConfirmedValue;
        double IRangeValueProvider.Minimum => owner.Minimum;
        double IRangeValueProvider.Maximum => owner.Maximum;
        double IRangeValueProvider.SmallChange => owner.SmallChange;
        double IRangeValueProvider.LargeChange => owner.LargeChange;

        void IRangeValueProvider.SetValue(double value)
        {
            if (!owner.IsEnabled)
                throw new ElementNotEnabledException();
            if (!double.IsFinite(value) || value < owner.Minimum || value > owner.Maximum || value != Math.Round(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (owner.IsAdjustmentBlocked)
                owner.ValueRequested?.Invoke(owner, new RoutedPropertyChangedEventArgs<double>(owner.ConfirmedValue, value));
            else
                owner.Value = value;
        }
    }
}
