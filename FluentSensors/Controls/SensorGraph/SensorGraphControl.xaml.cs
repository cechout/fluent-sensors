using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using System;
using System.Collections.ObjectModel;

using FluentSensors.Common.Sensors;
using FluentSensors.Diagnostics;


namespace FluentSensors.Controls.SensorGraph
{
    // self-contained graph control that owns all LiveCharts internals
    // consumers only bind Values, AccentColor, ManualYMax, IsAutoScaled, ThresholdValue, ThresholdDirection,
    // ThresholdColor, ThresholdLabelAlwaysVisible, LabelFollowsPointer

    // split across 3 files:
    // SensorGraphControl.xaml.cs (this file): fields, constructor, bindings, all DependencyProperties
    // SensorGraphControl.Rendering.cs: color / section calculation (ApplyStroke, RebuildSections, ...)
    // SensorGraphControl.Hover.cs: pointer hover interaction
    public sealed partial class SensorGraphControl : UserControl
    {
        // === fields ===

        private readonly Axis _yAxis;
        private readonly Axis _xAxis;
        private readonly SolidColorPaint _crosshairPaint;
        private readonly StepLineSeries<double?> _lineSeries;
        private bool _isPointerOverChart = false;
        private Windows.Foundation.Point _lastPointerPosition;
        private readonly DispatcherTimer _thresholdLabelTimer;
        private bool _isLoaded;


        // === constructor ===

        public SensorGraphControl()
        {
            InitializeComponent();

            // the LiveCharts ISeries definition
            _lineSeries = new StepLineSeries<double?>
            {
                Values = new ObservableCollection<double?>(),
                GeometrySize = 0,
                DataPadding = new LvcPoint(0, 0)
            };
            Series = new ISeries[] { _lineSeries };

            // the LiveCharts y-axis definition
            _yAxis = new Axis
            {
                IsVisible = false,
                MinLimit = 0,
                MaxLimit = null
            };
            YAxes = new ICartesianAxis[] { _yAxis };

            // custom x-axis line following the pointer
            _crosshairPaint = new SolidColorPaint(SKColors.Gray.WithAlpha(180))
            {
                StrokeThickness = 1,
                PathEffect = new DashEffect(new float[] { 3, 3 })
            };
            _xAxis = new Axis
            {
                IsVisible = false,
                CrosshairPaint = _crosshairPaint,
                CrosshairLabelsPaint = null,
                CrosshairSnapEnabled = false
            };
            XAxes = new ICartesianAxis[] { _xAxis };

            _thresholdLabelTimer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(2) };
            _thresholdLabelTimer.Tick += (s, e) =>
            {
                _thresholdLabelTimer.Stop();
                ThresholdValueLabelBorder.Visibility = Visibility.Collapsed;
            };

            Chart.PointerMoved += OnChartPointerMoved;
            Chart.PointerExited += OnChartPointerExited;
            Chart.UpdateStarted += Chart_UpdateStarted;

            // initial visuals and threshold state
            ApplyStroke();
            RebuildSections();
            ApplyCardBackground();
        }


        // === livecharts binding surfaces ===

        // (consumed directly by <lvc:CartesianChart> in SensorGraphControl.xaml)
        public ISeries[] Series { get; }
        public ICartesianAxis[] XAxes { get; }
        public ICartesianAxis[] YAxes { get; }
        public RectangularSection[] Sections { get; private set; } = new RectangularSection[0];
        public LiveChartsCore.Measure.Margin ChartMargin { get; } = new LiveChartsCore.Measure.Margin(0);


        // === dependency properties ===

        // DependencyProperty: Values 
        public ObservableCollection<double?> Values
        {
            get => (ObservableCollection<double?>)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }

        public static readonly DependencyProperty ValuesProperty =
            DependencyProperty.Register(
                nameof(Values),
                typeof(ObservableCollection<double?>),
                typeof(SensorGraphControl),
                new PropertyMetadata(null, OnValuesChanged));

        private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SensorGraphControl g) return;

            // stop listening to the previous Values list (the one before this change)
            if (e.OldValue is ObservableCollection<double?> oldValues)
            {
                oldValues.CollectionChanged -= g.OnValuesCollectionChanged;
            }

            // when the new value is null (e.g. this sensor does not exist on the currently bound hardware
            // instance), fall back to an empty collection instead of silently keeping whatever was there
            // before
            // without this, a null Values would leave the chart permanently pointed at the *previous* ViewModels
            // live data, since a plain "is ObservableCollection<double?>" pattern match on null simply fails and
            // skips the update entirely
            var effectiveValues = e.NewValue as ObservableCollection<double?> ?? new ObservableCollection<double?>();
            g._lineSeries.Values = effectiveValues;
            effectiveValues.CollectionChanged += g.OnValuesCollectionChanged;
            g.ApplyStroke();
        }

        // runs every time a data point is added or removed (i.e. every AddDataPoint call)
        private void OnValuesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            ApplyStroke();
            RebuildSections();

            if (_isPointerOverChart)
            {
                UpdateHoverAtPointer();
            }

            if (ThresholdValue is not null && ThresholdValueLabelBorder.Visibility == Visibility.Visible)
            {
                PositionThresholdLabel();
            }
        }


        // DependencyProperty: AccentColor
        public Windows.UI.Color AccentColor
        {
            get => (Windows.UI.Color)GetValue(AccentColorProperty);
            set => SetValue(AccentColorProperty, value);
        }
        public static readonly DependencyProperty AccentColorProperty =
            DependencyProperty.Register(
                nameof(AccentColor),
                typeof(Windows.UI.Color),
                typeof(SensorGraphControl),
                new PropertyMetadata(Windows.UI.Color.FromArgb(255, 0, 120, 212), OnAccentColorChanged));

        private static void OnAccentColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorGraphControl g && e.NewValue is Windows.UI.Color c)
            {
                g.ApplyStroke();
            }
        }


        // DependencyProperty: IsAutoScaled
        public bool IsAutoScaled
        {
            get => (bool)GetValue(IsAutoScaledProperty);
            set => SetValue(IsAutoScaledProperty, value);
        }

        public static readonly DependencyProperty IsAutoScaledProperty =
            DependencyProperty.Register(
                nameof(IsAutoScaled),
                typeof(bool),
                typeof(SensorGraphControl),
                new PropertyMetadata(true, OnScaleChanged));


        // DependencyProperty: ManualYMax
        public double ManualYMax
        {
            get => (double)GetValue(ManualYMaxProperty);
            set => SetValue(ManualYMaxProperty, value);
        }

        public static readonly DependencyProperty ManualYMaxProperty =
            DependencyProperty.Register(
                nameof(ManualYMax),
                typeof(double),
                typeof(SensorGraphControl),
                new PropertyMetadata(100.0, OnScaleChanged));

        // IsAutoScaled and ManualYMax both control the same thing: the y-axis maximum
        // so either one changing needs to update the axis and recolor the graph
        private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorGraphControl g)
            {
                g._yAxis.MaxLimit = g.IsAutoScaled ? (double?)null : g.ManualYMax;
                g.ApplyStroke(); // y-range change moves the thresholds relative position
                g.RebuildSections();
                g.ShowThresholdLabelBriefly();
            }
        }


        // DependencyProperty: ThresholdValue 
        public double? ThresholdValue
        {
            get => (double?)GetValue(ThresholdValueProperty);
            set => SetValue(ThresholdValueProperty, value);
        }

        public static readonly DependencyProperty ThresholdValueProperty =
            DependencyProperty.Register(
                nameof(ThresholdValue),
                typeof(double?),
                typeof(SensorGraphControl),
                new PropertyMetadata(null, OnThresholdChanged));

        private static void OnThresholdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorGraphControl g)
            {
                g.RebuildSections();
                g.ApplyStroke();
                g.ShowThresholdLabelBriefly();
            }
        }


        // DependencyProperty: ThresholdDirection
        public ThresholdDirection ThresholdDirection
        {
            get => (ThresholdDirection)GetValue(ThresholdDirectionProperty);
            set => SetValue(ThresholdDirectionProperty, value);
        }
        public static readonly DependencyProperty ThresholdDirectionProperty =
            DependencyProperty.Register(
                nameof(ThresholdDirection),
                typeof(ThresholdDirection),
                typeof(SensorGraphControl),
                new PropertyMetadata(ThresholdDirection.Above, OnThresholdVisualsChanged));


        // DependencyProperty: ThresholdColor
        public Windows.UI.Color ThresholdColor
        {
            get => (Windows.UI.Color)GetValue(ThresholdColorProperty);
            set
            {
                // ignore duplicate Set calls; without this, the ColorPickers TwoWay binding
                // can round-trip back into this setter and cause a StackOverflow
                var current = (Windows.UI.Color)GetValue(ThresholdColorProperty);
                if (current == value) return;
                SetValue(ThresholdColorProperty, value);
            }
        }

        public static readonly DependencyProperty ThresholdColorProperty =
            DependencyProperty.Register(
                nameof(ThresholdColor),
                typeof(Windows.UI.Color),
                typeof(SensorGraphControl),
                new PropertyMetadata(Windows.UI.Color.FromArgb(255, 220, 50, 50), OnThresholdVisualsChanged));

        // shared callback for ThresholdDirection and ThresholdColor: both need a full repaint
        private static void OnThresholdVisualsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (Equals(e.OldValue, e.NewValue)) return; // skip if nothing actually changed 

            if (d is SensorGraphControl g)
            {
                g.RebuildSections();
                g.ApplyStroke();
                g.ShowThresholdLabelBriefly();
            }
        }


        // DependencyProperty: ThresholdLabelAlwaysVisible
        public bool ThresholdLabelAlwaysVisible
        {
            get => (bool)GetValue(ThresholdLabelAlwaysVisibleProperty);
            set => SetValue(ThresholdLabelAlwaysVisibleProperty, value);
        }

        public static readonly DependencyProperty ThresholdLabelAlwaysVisibleProperty =
            DependencyProperty.Register(
                nameof(ThresholdLabelAlwaysVisible),
                typeof(bool),
                typeof(SensorGraphControl),
                new PropertyMetadata(false, OnThresholdLabelAlwaysVisibleChanged));

        private static void OnThresholdLabelAlwaysVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorGraphControl g) g.ShowThresholdLabelBriefly();
        }


        // DependencyProperty: LabelFollowsPointer
        public bool LabelFollowsPointer
        {
            get => (bool)GetValue(LabelFollowsPointerProperty);
            set => SetValue(LabelFollowsPointerProperty, value);
        }

        public static readonly DependencyProperty LabelFollowsPointerProperty =
            DependencyProperty.Register(
                nameof(LabelFollowsPointer),
                typeof(bool),
                typeof(SensorGraphControl),
                new PropertyMetadata(false));


        // DependencyProperty: LabelText
        public string LabelText
        {
            get => (string)GetValue(LabelTextProperty);
            set => SetValue(LabelTextProperty, value);
        }

        public static readonly DependencyProperty LabelTextProperty =
            DependencyProperty.Register(
                nameof(LabelText),
                typeof(string),
                typeof(SensorGraphControl),
                new PropertyMetadata(string.Empty, OnLabelChanged));


        // DependencyProperty: IsLabelVisible
        public bool IsLabelVisible
        {
            get => (bool)GetValue(IsLabelVisibleProperty);
            set => SetValue(IsLabelVisibleProperty, value);
        }

        public static readonly DependencyProperty IsLabelVisibleProperty =
            DependencyProperty.Register(
                nameof(IsLabelVisible),
                typeof(bool),
                typeof(SensorGraphControl),
                new PropertyMetadata(false, OnLabelChanged));

        // shared callback for LabelText and IsLabelVisible: purely a static text overlay, no chart repaint needed
        private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SensorGraphControl g) return;

            g.GraphLabelText.Text = g.LabelText;
            g.GraphLabelText.Visibility = g.IsLabelVisible && !string.IsNullOrEmpty(g.LabelText)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }


        // DependencyProperty: CardBackgroundOverride
        // null = no override, uses the normal themed VisualState (CardBackgroundVisible, which stays theme-reactive
        // since its Setters use ThemeResource brushes)
        // any explicit Color, including a fully transparent one, is applied directly as a plain SolidColorBrush instead,
        // bypassing the VisualState entirely - covers both a hard override color (e.g. highlighting the selected item
        // in the Performance page's hardware sidebar) and full transparency (the previous ShowCardBackground=false
        // behavior, now expressed as an override of Colors.Transparent - see SensorPanelControl.ShowGraphCardBackground)
        public Windows.UI.Color? CardBackgroundOverride
        {
            get => (Windows.UI.Color?)GetValue(CardBackgroundOverrideProperty);
            set => SetValue(CardBackgroundOverrideProperty, value);
        }

        public static readonly DependencyProperty CardBackgroundOverrideProperty =
            DependencyProperty.Register(
                nameof(CardBackgroundOverride),
                typeof(Windows.UI.Color?),
                typeof(SensorGraphControl),
                new PropertyMetadata(null, OnCardBackgroundOverrideChanged));

        private static void OnCardBackgroundOverrideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorGraphControl g) g.ApplyCardBackground();
        }

        // applies CardBackgroundOverride's current value; factored out so both the constructor (initial state) and the
        // property-changed callback share the same logic
        private void ApplyCardBackground()
        {
            if (CardBackgroundOverride is Windows.UI.Color color)
            {
                CardBorder.Background = new SolidColorBrush(color);
            }
            else
            {
                VisualStateManager.GoToState(this, "CardBackgroundVisible", false);
            }
        }


        // DependencyProperty: IsHoverEnabled
        // fully disables pointer hover interaction when false: circle + value label (OnChartPointerMoved /
        // OnChartPointerExited) are unsubscribed entirely instead of just early-returning inside them
        public bool IsHoverEnabled
        {
            get => (bool)GetValue(IsHoverEnabledProperty);
            set => SetValue(IsHoverEnabledProperty, value);
        }

        public static readonly DependencyProperty IsHoverEnabledProperty =
            DependencyProperty.Register(
                nameof(IsHoverEnabled),
                typeof(bool),
                typeof(SensorGraphControl),
                new PropertyMetadata(true, OnIsHoverEnabledChanged));

        private static void OnIsHoverEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SensorGraphControl g) return;

            bool enabled = (bool)e.NewValue;

            if (enabled)
            {
                g.Chart.PointerMoved += g.OnChartPointerMoved;
                g.Chart.PointerExited += g.OnChartPointerExited;
            }
            else
            {
                g.Chart.PointerMoved -= g.OnChartPointerMoved;
                g.Chart.PointerExited -= g.OnChartPointerExited;
                g.HideHoverElements(); // clears any hover state left over from before being disabled
            }

            // detach LiveCharts own crosshair paint too, so it stops tracking the pointer internally as well
            g._xAxis.CrosshairPaint = enabled ? g._crosshairPaint : null;

            // with hover off, the chart no longer needs any pointer input of its own
            // Taking it fully out of hit-testing lets clicks pass straight through
            g.Chart.IsHitTestVisible = enabled;
        }


        // === event handlers ===

        // fires once, the first time LiveCharts has actually built its internal render context and drawn a real frame;
        // used by MainWindow to prewarm the native SkiaSharp/LiveChartsCore pipeline during the splash screen
        public event EventHandler ChartReady;

        // LiveCharts only builds its internal scale/draw context on the first real measure pass;
        // UpdateStarted fires once that has happened (Loaded fires too early, before the chart is actually ready)
        private void Chart_UpdateStarted(LiveChartsCore.Kernel.Sketches.IChartView chart)
        {
            Chart.UpdateStarted -= Chart_UpdateStarted;
            _isLoaded = true;
            ShowThresholdLabelBriefly();
        }
    }
}
    
