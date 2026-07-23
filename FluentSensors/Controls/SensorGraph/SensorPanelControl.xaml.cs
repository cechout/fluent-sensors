using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using FluentSensors.Common;


namespace FluentSensors.Controls.SensorGraph
{
    public sealed partial class SensorPanelControl : UserControl
    {
        public SensorPanelControl()
        {
            InitializeComponent();
        }


        // === dependency properties ===

        public SensorGraphViewModel ViewModel
        {
            get => (SensorGraphViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(SensorGraphViewModel),
                typeof(SensorPanelControl),
                new PropertyMetadata(null, OnOverrideChanged));

        public SensorPanelMode Mode
        {
            get => (SensorPanelMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        // no changed-callback needed: Mode is set once via a static XAML attribute per instance and never
        // changes at runtime in current usage
        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(
                nameof(Mode),
                typeof(SensorPanelMode),
                typeof(SensorPanelControl),
                new PropertyMetadata(SensorPanelMode.Standard));

        // 0 = no override
        // (a real graph never has 0 data points, so this doubles as a safe sentinel)
        public int GraphDataPointsOverride
        {
            get => (int)GetValue(GraphDataPointsOverrideProperty);
            set => SetValue(GraphDataPointsOverrideProperty, value);
        }

        public static readonly DependencyProperty GraphDataPointsOverrideProperty =
            DependencyProperty.Register(
                nameof(GraphDataPointsOverride),
                typeof(int),
                typeof(SensorPanelControl),
                new PropertyMetadata(0, OnOverrideChanged));

        // Inherit = no override
        // (this sensors persisted/global IsAutoScaled state is used as-is)
        public BoolOverride IsAutoScaledOverride
        {
            get => (BoolOverride)GetValue(IsAutoScaledOverrideProperty);
            set => SetValue(IsAutoScaledOverrideProperty, value);
        }

        public static readonly DependencyProperty IsAutoScaledOverrideProperty =
            DependencyProperty.Register(
                nameof(IsAutoScaledOverride),
                typeof(BoolOverride),
                typeof(SensorPanelControl),
                new PropertyMetadata(BoolOverride.Inherit, OnOverrideChanged));

        // NaN = no override
        public double ManualYMaxOverride
        {
            get => (double)GetValue(ManualYMaxOverrideProperty);
            set => SetValue(ManualYMaxOverrideProperty, value);
        }

        public static readonly DependencyProperty ManualYMaxOverrideProperty =
            DependencyProperty.Register(
                nameof(ManualYMaxOverride),
                typeof(double),
                typeof(SensorPanelControl),
                new PropertyMetadata(double.NaN, OnOverrideChanged));

        // pure visual pass-through to SensorGraphControl.ThresholdLabelAlwaysVisible; no ViewModel coupling, so this needs
        // no override/decoupling logic; it never persists anywhere to begin with
        public bool ThresholdLabelAlwaysVisible
        {
            get => (bool)GetValue(ThresholdLabelAlwaysVisibleProperty);
            set => SetValue(ThresholdLabelAlwaysVisibleProperty, value);
        }

        public static readonly DependencyProperty ThresholdLabelAlwaysVisibleProperty =
            DependencyProperty.Register(
                nameof(ThresholdLabelAlwaysVisible),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(true));

        // fires whenever ViewModel itself changes, or any of the three override properties change; re-applies all of them
        // together so the final state is always correct regardless of the order XAML happens to set these attributes in
        private static void OnOverrideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorPanelControl panel)
            {
                panel.ApplyOverridesToViewModel();
            }
        }

        private void ApplyOverridesToViewModel()
        {
            int? dataPoints = GraphDataPointsOverride > 0 ? GraphDataPointsOverride : (int?)null;

            bool? isAutoScaled = IsAutoScaledOverride switch
            {
                BoolOverride.True => true,
                BoolOverride.False => false,
                _ => null
            };

            double? manualYMax = double.IsNaN(ManualYMaxOverride) ? (double?)null : ManualYMaxOverride;

            ViewModel?.ApplyViewOverrides(dataPoints, isAutoScaled, manualYMax);
        }


        // === bindable helper surfaces ===

        // title is shown in Performance and Minimal (Widget shows the status header instead, which already
        // contains the sensor name)
        private Visibility GetTitleVisibility(SensorPanelMode mode)
        {
            return mode == SensorPanelMode.Standard ? Visibility.Collapsed : Visibility.Visible;
        }

        private Visibility GetStatusHeaderVisibility(SensorPanelMode mode)
        {
            return mode == SensorPanelMode.Standard ? Visibility.Visible : Visibility.Collapsed;
        }

        private Visibility GetYAxisControlsVisibility(SensorPanelMode mode, Visibility controlPanelVisibility)
        {
            return mode == SensorPanelMode.Standard ? controlPanelVisibility : Visibility.Collapsed;
        }

        private Visibility GetThresholdControlsVisibility(SensorPanelMode mode, Visibility controlPanelVisibility)
        {
            return mode == SensorPanelMode.Minimal ? Visibility.Collapsed : controlPanelVisibility;
        }


        // === event handlers ===

        private void GraphControl_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (Mode == SensorPanelMode.Performance)
            {
                ViewModel?.ToggleControlPanel();
            }
        }
    }
}