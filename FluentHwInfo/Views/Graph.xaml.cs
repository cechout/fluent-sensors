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


namespace FluentHwInfo.Views
{
    // side of the threshold that gets colored differently
    public enum ThresholdDirection
    {
        Above, // values greater than threshold are colored
        Below // values less than threshold are colored
    }


    // self-contained graph control that owns all LiveCharts internals
    // consumers only bind Values, AccentColor, ManualYMax, IsAutoScaled, ThresholdValue
    public sealed partial class Graph : UserControl
    {
        // fields
        private readonly Axis _yAxis;
        private readonly StepLineSeries<double?> _lineSeries;
        private bool _isPointerOverChart = false;
        private Windows.Foundation.Point _lastPointerPosition;


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

            if (_isPointerOverChart)
            {
                UpdateHoverAtPointer();
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


        // color calculation
        // rebuilds the colors of the graph line (Stroke) and the area under it (Fill)
        // called whenever anything changes that affects color: values, accent color, threshold, y-range
        private void ApplyStroke()
        {
            if (_lineSeries == null) return; // guard: called before constructor finishes

            var accent = new SKColor(AccentColor.R, AccentColor.G, AccentColor.B);

            // no threshold set: flat single-color line and area
            if (ThresholdValue is null)
            {
                _lineSeries.Fill = new LinearGradientPaint(
                    new[] { accent.WithAlpha(38), accent.WithAlpha(38) },
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1));

                _lineSeries.Stroke = new SolidColorPaint(accent.WithAlpha(204)) { StrokeThickness = 1 };
                return;
            }

            // colors the graph line: split at the thresholds y-position
            var threshold = new SKColor(ThresholdColor.R, ThresholdColor.G, ThresholdColor.B);

            double yMax = ComputeCurrentYMax();
            if (yMax <= 0) yMax = 1;

            const double strokeOffsetPixels = 0.6; // moves the lines color-change point up by this many pixels
            double chartHeight = Chart?.ActualHeight ?? 80.0;
            double yRatio = 1.0 - (ThresholdValue.Value / yMax) + (strokeOffsetPixels / chartHeight);
            yRatio = System.Math.Clamp(yRatio, 0.0, 1.0);

            SKColor topColor, bottomColor;
            if (ThresholdDirection == ThresholdDirection.Above)
            {
                topColor = threshold;
                bottomColor = accent;
            }
            else
            {
                topColor = accent;
                bottomColor = threshold;
            }

            _lineSeries.Stroke = new LinearGradientPaint(
                new[] { topColor.WithAlpha(204), topColor.WithAlpha(204), bottomColor.WithAlpha(204), bottomColor.WithAlpha(204) },
                new SKPoint(0.5f, 0),
                new SKPoint(0.5f, 1),
                new[] { 0f, (float)yRatio, (float)yRatio, 1f })
            {
                StrokeThickness = 1
            };

            // colors the area under the line: cuts transparent gaps during alarm zones
            // the area itself never turns red, it just becomes invisible during alarm zones, so the red RectangularSection box
            // (built in RebuildSections) shows through cleanly
            var runs = ComputeThresholdRuns();

            if (runs.Count == 0 || Values is null || Values.Count == 0)
            {
                // no alarm zones right now: flat area color
                _lineSeries.Fill = new LinearGradientPaint(
                    new[] { accent.WithAlpha(38), accent.WithAlpha(38) },
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1));
                return;
            }

            // lastIndex turns a data point index(e.g. 12) into a 0 - 1 position for the gradient
            int lastIndex = Values.Count - 1;
            if (lastIndex <= 0) lastIndex = 1; // guard against divide-by-zero

            // colorList[i] is the color that starts at position stopList[i]; together they define the gradient
            var colorList = new System.Collections.Generic.List<SKColor>();
            var stopList = new System.Collections.Generic.List<float>();

            // gradient starts on the left edge with the normal (non-alarm) area color
            colorList.Add(accent.WithAlpha(38));
            stopList.Add(0f);

            foreach (var (start, end) in runs)
            {
                // shift the area to be removed
                // bevore: start-0.5 and end+0.5
                // now:    start+0.0 and end+1.0
                float startRatio = (float)System.Math.Clamp((start + 0.0) / lastIndex, 0.0, 1.0);
                float endRatio = (float)System.Math.Clamp((end + 1.0) / lastIndex, 0.0, 1.0);

                // hard drop to fully transparent at the start of the alarm zone
                colorList.Add(accent.WithAlpha(38));
                stopList.Add(startRatio);
                colorList.Add(accent.WithAlpha(0));
                stopList.Add(startRatio);

                // hard return to normal color at the end of the alarm zone
                colorList.Add(accent.WithAlpha(0));
                stopList.Add(endRatio);
                colorList.Add(accent.WithAlpha(38));
                stopList.Add(endRatio);
            }

            colorList.Add(accent.WithAlpha(38));
            stopList.Add(1f);

            _lineSeries.Fill = new LinearGradientPaint(
                colorList.ToArray(),
                new SKPoint(0, 0.5f), // horizontal gradient: left -> right
                new SKPoint(1, 0.5f),
                stopList.ToArray());
        }


        // draws the horizontal threshold line, plus one full-height red box per alarm zone
        private void RebuildSections()
        {
            if (ThresholdValue is null)
            {
                Sections = new RectangularSection[0];
                if (Chart != null) Chart.Sections = Sections;
                return;
            }

            var sections = new System.Collections.Generic.List<RectangularSection>();
            var thresholdSk = new SKColor(ThresholdColor.R, ThresholdColor.G, ThresholdColor.B);

            // the horizontal threshold reference line
            var lineStroke = new SolidColorPaint(thresholdSk.WithAlpha(180))
            {
                StrokeThickness = 1,
                //PathEffect = new DashEffect(new float[] { 4, 3 }) // dashed line
            };
            sections.Add(new RectangularSection
            {
                Yi = ThresholdValue.Value,
                Yj = ThresholdValue.Value,
                Stroke = lineStroke,
                Fill = null
            });

            // one full-height red box per alarm zone
            var runs = ComputeThresholdRuns();
            var boxFill = new SolidColorPaint(thresholdSk.WithAlpha(38));  // same 15% alpha as normal fill

            foreach (var (start, end) in runs)
            {
                sections.Add(new RectangularSection
                {
                    // shift the area to be filled
                    // before: start-0.5 and end+0.5
                    // now:    start+0.0 and end+1.0
                    Xi = start - 0.0,
                    Xj = end + 1.0,
                    Yi = null, // y-range: null on both = full height of the chart
                    Yj = null,
                    Fill = boxFill,
                    Stroke = null
                });
            }

            Sections = sections.ToArray();
            if (Chart != null) Chart.Sections = Sections;
        }


        // returns the current highest value on the y-axis:
        // the fixed ManualYMax value, or the highest visible data point when auto-scaled
        private double ComputeCurrentYMax()
        {
            if (!IsAutoScaled) return ManualYMax;

            if (Values == null || Values.Count == 0) return 100;

            double max = 0;
            foreach (var v in Values)
            {
                if (v.HasValue && v.Value > max) max = v.Value;
            }

            return max <= 0 ? 100 : max;  // fall back to a sensible range if all values are 0
        }

        // finds every time range where the value is over (or under, see ThresholdDirection) the threshold
        // returns one (startIndex, endIndex) pair per alarm zone
        private List<(int Start, int End)> ComputeThresholdRuns()
        {
            var runs = new List<(int, int)>();

            if (ThresholdValue is null || Values is null || Values.Count == 0)
                return runs;

            double threshold = ThresholdValue.Value;
            bool alarmAbove = ThresholdDirection == ThresholdDirection.Above;

            int? runStart = null;

            for (int i = 0; i < Values.Count; i++)
            {
                var v = Values[i];
                bool isAlarm = v.HasValue && (alarmAbove ? v.Value > threshold : v.Value < threshold);

                if (isAlarm && runStart is null)
                {
                    runStart = i;  // alarm zone begins here
                }
                else if (!isAlarm && runStart is not null)
                {
                    runs.Add((runStart.Value, i - 1));  // alarm zone ended at the previous index
                    runStart = null;
                }
            }

            // the data ends while still inside an alarm zone -> close it at the last index
            if (runStart is not null)
            {
                runs.Add((runStart.Value, Values.Count - 1));
            }

            return runs;
        }


        // updates hover circle + label position and value whenever the pointer moves over the chart
        private void OnChartPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _lastPointerPosition = e.GetCurrentPoint(Chart).Position;
            UpdateHoverAtPointer();
        }

        // re-usable: called on pointer move, and also on every new data point while the
        // pointer is still sitting still over the chart, so the hover value keeps tracking
        private void UpdateHoverAtPointer()
        {
            if (Values is null || Values.Count == 0)
            {
                HideHoverElements();
                return;
            }

            var position = _lastPointerPosition;
            var dataPoint = Chart.ScalePixelsToData(new LvcPointD(position.X, position.Y));

            int index = (int)System.Math.Floor(dataPoint.X);
            if (index < 0 || index >= Values.Count)
            {
                HideHoverElements();
                return;
            }

            var value = Values[index];
            if (value is null)
            {
                HideHoverElements();
                return;
            }

            if (!_isPointerOverChart)
            {
                _isPointerOverChart = true;
                ShowHoverElements();
            }

            var valuePixels = Chart.ScaleDataToPixels(new LvcPointD(dataPoint.X, value.Value));

            Canvas.SetLeft(HoverCircle, position.X - HoverCircle.Width / 2);
            Canvas.SetTop(HoverCircle, valuePixels.Y - HoverCircle.Height / 2);

            // pick which Y coordinate the label follows, based on LabelFollowsPointer
            double labelY = LabelFollowsPointer ? position.Y : valuePixels.Y;
            Canvas.SetLeft(HoverLabelBorder, position.X + 10);
            Canvas.SetTop(HoverLabelBorder, labelY - 14);
            HoverLabelText.Text = value.Value.ToString("0.0");
        }

        // hides the hover circle and label when the pointer leaves the chart area or lands on invalid data
        private void OnChartPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            HideHoverElements();
        }

        private void HideHoverElements()
        {
            _isPointerOverChart = false;
            HoverCircle.Visibility = Visibility.Collapsed;
            HoverLabelBorder.Visibility = Visibility.Collapsed;
        }

        // colors the hover circle + label to match the current accent color, then reveals them
        private void ShowHoverElements()
        {
            var accent = AccentColor;

            HoverCircle.Fill = new SolidColorBrush(accent);
            HoverCircle.Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(220, 255, 255, 255));
            HoverCircle.Visibility = Visibility.Visible;

            HoverLabelBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(220, accent.R, accent.G, accent.B));
            HoverLabelText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
            HoverLabelBorder.Visibility = Visibility.Visible;
        }
    }
}