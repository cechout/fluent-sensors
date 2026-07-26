using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using FluentSensors.Common.UI;
using FluentSensors.Common.Sensors;


namespace FluentSensors.Controls.SensorGraph
{
    // composable chrome around a SensorGraphControl:
    // every optional piece (title row, status row, y-axis/threshold controls, graph-tap-to-toggle, ...) is its own
    // independent property instead of a fixed set of presets, so any combination a consumer needs can be set directly
    // in XAML without touching this class
    // UseThresholdFlyout is the one exception: when true, it fully replaces ShowYAxisControls/ShowThresholdControls
    // (forces both to Collapsed) instead of combining with them; see GetYAxisControlsVisibility/GetThresholdControlsVisibility
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

        // separate title row above everything else, showing just the sensor name
        public bool ShowTitleRow
        {
            get => (bool)GetValue(ShowTitleRowProperty);
            set => SetValue(ShowTitleRowProperty, value);
        }

        public static readonly DependencyProperty ShowTitleRowProperty =
            DependencyProperty.Register(
                nameof(ShowTitleRow),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // status row shows name + unit combined instead of just the name (no effect if ShowStatusRow is false)
        public bool ShowUnitInStatusRow
        {
            get => (bool)GetValue(ShowUnitInStatusRowProperty);
            set => SetValue(ShowUnitInStatusRowProperty, value);
        }

        public static readonly DependencyProperty ShowUnitInStatusRowProperty =
            DependencyProperty.Register(
                nameof(ShowUnitInStatusRow),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // inline status row (toggle button, Y-max value, sensor name, current value) with its own dedicated toggle button
        public bool ShowStatusRow
        {
            get => (bool)GetValue(ShowStatusRowProperty);
            set => SetValue(ShowStatusRowProperty, value);
        }

        public static readonly DependencyProperty ShowStatusRowProperty =
            DependencyProperty.Register(
                nameof(ShowStatusRow),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // tapping the graph itself toggles the control panel; independent of ShowStatusRow so it also works when
        // theres no dedicated toggle button (e.g. compact layouts with no status row at all)
        public bool TogglePanelOnGraphTap
        {
            get => (bool)GetValue(TogglePanelOnGraphTapProperty);
            set => SetValue(TogglePanelOnGraphTapProperty, value);
        }

        public static readonly DependencyProperty TogglePanelOnGraphTapProperty =
            DependencyProperty.Register(
                nameof(TogglePanelOnGraphTap),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // Y-axis scaling buttons (increase/decrease/auto) inside the control panel
        public bool ShowYAxisControls
        {
            get => (bool)GetValue(ShowYAxisControlsProperty);
            set => SetValue(ShowYAxisControlsProperty, value);
        }

        public static readonly DependencyProperty ShowYAxisControlsProperty =
            DependencyProperty.Register(
                nameof(ShowYAxisControls),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // threshold buttons (increase/decrease/enable/direction/color) inside the control panel
        public bool ShowThresholdControls
        {
            get => (bool)GetValue(ShowThresholdControlsProperty);
            set => SetValue(ShowThresholdControlsProperty, value);
        }

        public static readonly DependencyProperty ShowThresholdControlsProperty =
            DependencyProperty.Register(
                nameof(ShowThresholdControls),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // shows a small "sensor name (unit)" label directly inside the graph itself, top-left, in gray
        // (independent of ShowTitleRow/ShowStatusRow, e.g. for compact layouts with neither)
        public bool ShowGraphLabel
        {
            get => (bool)GetValue(ShowGraphLabelProperty);
            set => SetValue(ShowGraphLabelProperty, value);
        }

        public static readonly DependencyProperty ShowGraphLabelProperty =
            DependencyProperty.Register(
                nameof(ShowGraphLabel),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // replaces the entire control panel (ShowYAxisControls/ShowThresholdControls, regardless of their own value)
        // with a single compact flyout trigger reusing the threshold editor flyout from SensorRowControl
        public bool UseThresholdFlyout
        {
            get => (bool)GetValue(UseThresholdFlyoutProperty);
            set => SetValue(UseThresholdFlyoutProperty, value);
        }

        public static readonly DependencyProperty UseThresholdFlyoutProperty =
            DependencyProperty.Register(
                nameof(UseThresholdFlyout),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // overrides the graphs line/section color for this specific instance, regardless of the global accent/custom
        // color setting; Colors.Transparent (Alpha 0) = no override, since a real accent color is never fully transparent
        public Windows.UI.Color GraphColorOverride
        {
            get => (Windows.UI.Color)GetValue(GraphColorOverrideProperty);
            set => SetValue(GraphColorOverrideProperty, value);
        }

        public static readonly DependencyProperty GraphColorOverrideProperty =
            DependencyProperty.Register(
                nameof(GraphColorOverride),
                typeof(Windows.UI.Color),
                typeof(SensorPanelControl),
                new PropertyMetadata(Windows.UI.Color.FromArgb(0, 0, 0, 0)));

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

        private Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        // status row shows either the plain sensor name, or name+unit combined, depending on ShowUnitInStatusRow
        private string GetStatusRowTitle(bool showUnit, string name, string nameWithUnit)
        {
            return showUnit ? nameWithUnit : name;
        }

        // Y-axis and threshold controls both live inside the same toggleable control panel; UseThresholdFlyout replaces
        // both entirely when active, so it forces this to Collapsed regardless of the individual Show*Controls properties
        private Visibility GetYAxisControlsVisibility(bool showYAxisControls, bool useThresholdFlyout, Visibility controlPanelVisibility)
        {
            return showYAxisControls && !useThresholdFlyout ? controlPanelVisibility : Visibility.Collapsed;
        }

        private Visibility GetThresholdControlsVisibility(bool showThresholdControls, bool useThresholdFlyout, Visibility controlPanelVisibility)
        {
            return showThresholdControls && !useThresholdFlyout ? controlPanelVisibility : Visibility.Collapsed;
        }

        // combines the automatic per-sensor color (resolved via global accent/custom settings) with this instances
        // optional override; Alpha 0 on the override means "not set", since a real accent color is never fully transparent
        private Windows.UI.Color GetEffectiveGraphColor(Windows.UI.Color overrideColor, Windows.UI.Color autoColor)
        {
            return overrideColor.A == 0 ? autoColor : overrideColor;
        }


        // === event handlers ===

        private void GraphControl_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (TogglePanelOnGraphTap)
            {
                ViewModel?.ToggleControlPanel();
            }
        }
    }
}