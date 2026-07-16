using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using System.Collections.Generic;
using System.Collections.ObjectModel;


namespace FluentHwInfo.Controls
{
    // side of the threshold that gets colored differently
    public enum ThresholdDirection
    {
        Above, // values greater than threshold are colored
        Below // values less than threshold are colored
    }


    // self-contained graph control that owns all LiveCharts internals
    // consumers only bind Values, AccentColor, ManualYMax, IsAutoScaled, ThresholdValue

    // split across 3 files:
    // Graph.xaml.cs (this file): fields, constructor, bindings, all DependencyProperties
    // Graph.Rendering.cs: color / section calculation (ApplyStroke, RebuildSections, ...)
    // Graph.Hover.cs: pointer hover interaction
    public sealed partial class Graph : UserControl
    {
        // fields
        private readonly Axis _yAxis;
        private readonly StepLineSeries<double?> _lineSeries;
        private bool _isPointerOverChart = false;
        private Windows.Foundation.Point _lastPointerPosition;
        private readonly DispatcherTimer _thresholdLabelTimer;


        // constructor
        public Graph()
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
            var crosshairPaint = new SolidColorPaint(SKColors.Gray.WithAlpha(180))
            {
                StrokeThickness = 1,
                PathEffect = new DashEffect(new float[] { 3, 3 })
            };
            XAxes = new ICartesianAxis[]
            {
                new Axis
                {
                    IsVisible = false,
                    CrosshairPaint = crosshairPaint,
                    CrosshairLabelsPaint = null,
                    CrosshairSnapEnabled = false
                }
            };

            _thresholdLabelTimer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(2) };
            _thresholdLabelTimer.Tick += (s, e) =>
            {
                _thresholdLabelTimer.Stop();
                ThresholdValueLabelBorder.Visibility = Visibility.Collapsed;
            };

            Chart.PointerMoved += OnChartPointerMoved;
            Chart.PointerExited += OnChartPointerExited;

            // initial visuals and threshold state
            ApplyStroke();
            RebuildSections();
        }


        // LiveCharts binding surfaces exposed to XAML
        // (consumed directly by <lvc:CartesianChart> in Graph.xaml)
        public ISeries[] Series { get; }
        public ICartesianAxis[] XAxes { get; }
        public ICartesianAxis[] YAxes { get; }
        public RectangularSection[] Sections { get; private set; } = new RectangularSection[0];
        public LiveChartsCore.Measure.Margin ChartMargin { get; } = new LiveChartsCore.Measure.Margin(0);


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
                typeof(Graph),
                new PropertyMetadata(null, OnValuesChanged));

        private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Graph g) return;

            // stop listening to the previous Values list (the one before this change)
            if (e.OldValue is ObservableCollection<double?> oldValues)
            {
                oldValues.CollectionChanged -= g.OnValuesCollectionChanged;
            }

            // start listening to the new Values list, so new/removed data points update the graph
            if (e.NewValue is ObservableCollection<double?> newValues)
            {
                g._lineSeries.Values = newValues;
                newValues.CollectionChanged += g.OnValuesCollectionChanged;
                g.ApplyStroke();
            }
        }
        // runs every time a data point is added or removed (i.e. every AddDataPoint call)
        private void OnValuesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            ApplyStroke();
            RebuildSections();

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
                typeof(Graph),
                new PropertyMetadata(Windows.UI.Color.FromArgb(255, 0, 120, 212), OnAccentColorChanged));

        private static void OnAccentColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Graph g && e.NewValue is Windows.UI.Color c)
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
                typeof(Graph),
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
                typeof(Graph),
                new PropertyMetadata(100.0, OnScaleChanged));

        // IsAutoScaled and ManualYMax both control the same thing: the y-axis maximum
        // so either one changing needs to update the axis and recolor the graph
        private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Graph g)
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
                typeof(Graph),
                new PropertyMetadata(null, OnThresholdChanged));

        private static void OnThresholdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Graph g)
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
                typeof(Graph),
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
                typeof(Graph),
                new PropertyMetadata(Windows.UI.Color.FromArgb(255, 220, 50, 50), OnThresholdVisualsChanged));

        // shared callback for ThresholdDirection and ThresholdColor: both need a full repaint
        private static void OnThresholdVisualsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (Equals(e.OldValue, e.NewValue)) return; // skip if nothing actually changed 

            if (d is Graph g)
            {
                g.RebuildSections();
                g.ApplyStroke();
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
                typeof(Graph),
                new PropertyMetadata(false, OnThresholdLabelAlwaysVisibleChanged));

        private static void OnThresholdLabelAlwaysVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Graph g) g.ShowThresholdLabelBriefly();
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
                typeof(Graph),
                new PropertyMetadata(false));
    }
}