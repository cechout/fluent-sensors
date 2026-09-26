using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;


namespace FluentSensors.Controls.SensorRow
{
    // presents a whole sensor row to screen readers as one checkbox named after the sensor, because the row is what
    // toggles the selection; the checkbox drawn inside it is visual only and hidden from automation
    public partial class SensorRowAutomationPeer : FrameworkElementAutomationPeer, IToggleProvider
    {
        // === constructor ===

        public SensorRowAutomationPeer(SensorRowControl owner) : base(owner)
        {
        }

        private SensorRowControl Row => (SensorRowControl)Owner;


        // === automation peer overrides ===

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.CheckBox;

        protected override string GetClassNameCore() => nameof(SensorRowControl);

        protected override string GetNameCore() => Row.ViewModel?.Name ?? base.GetNameCore();

        // disabled rows stay visible but cannot be selected, so they are reported as disabled
        protected override bool IsEnabledCore() => Row.ViewModel?.IsDisabled != true;

        protected override object GetPatternCore(PatternInterface patternInterface)
        {
            if (patternInterface == PatternInterface.Toggle) return this;
            return base.GetPatternCore(patternInterface);
        }


        // === toggle pattern ===

        public ToggleState ToggleState => Row.ViewModel?.IsSelected == true ? ToggleState.On : ToggleState.Off;

        public void Toggle() => Row.ToggleSelection();

        // lets a running screen reader announce the new check state right away, whatever flipped it
        public void RaiseToggleStateChanged(bool isSelected)
        {
            RaisePropertyChangedEvent(
                TogglePatternIdentifiers.ToggleStateProperty,
                isSelected ? ToggleState.Off : ToggleState.On,
                isSelected ? ToggleState.On : ToggleState.Off);
        }
    }
}
