using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;


namespace FluentSensors.Controls.Threshold
{
    // presents the threshold badge to screen readers as a button that opens the threshold editor
    public partial class ThresholdFlyoutAutomationPeer : FrameworkElementAutomationPeer, IInvokeProvider
    {
        // === constructor ===

        public ThresholdFlyoutAutomationPeer(ThresholdFlyoutControl owner) : base(owner)
        {
        }

        private ThresholdFlyoutControl Badge => (ThresholdFlyoutControl)Owner;


        // === automation peer overrides ===

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string GetClassNameCore() => nameof(ThresholdFlyoutControl);

        protected override string GetNameCore() => Badge.AutomationName;

        protected override object GetPatternCore(PatternInterface patternInterface)
        {
            if (patternInterface == PatternInterface.Invoke) return this;
            return base.GetPatternCore(patternInterface);
        }


        // === invoke pattern ===

        public void Invoke() => Badge.ShowFlyout();
    }
}
