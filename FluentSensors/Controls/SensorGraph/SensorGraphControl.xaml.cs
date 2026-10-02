using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Microsoft.UI.Dispatching;
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
    // the sensor graph:
    // a self-contained graph that owns every LiveCharts internal; consumers bind values, colors, scaling,
    // threshold and label properties
    //
    // split across three files:
    // SensorGraphControl.xaml.cs - fields, constructor, bindings, every DependencyProperty
    // SensorGraphControl.Rendering.cs - colors and sections (ApplyStroke, RebuildSections)
    // SensorGraphControl.Hover.cs - pointer hover
    public sealed partial class SensorGraphControl : UserControl
    {
        // === fields ===

        private readonly Axis _yAxis;
        private readonly Axis _xAxis;
        private readonly SolidColorPaint _crosshairPaint;

        // stepline and smooth are two series types (StepLineSeries, LineSeries), so a style switch swaps the series;
        // both built once, one bound at a time (see ApplyLineStyle)
        private readonly StepLineSeries<double?> _stepSeries;
        private readonly LineSeries<double?> _smoothSeries;
        private ISeries _lineSeries; // the active one
        private bool _isPointerOverChart = false;
        private Windows.Foundation.Point _lastPointerPosition;
        private readonly DispatcherTimer _thresholdLabelTimer;
        private bool _isLoaded;

        // live rendering gate:
        // an off-screen graph is detached from its data, so LiveCharts does no per-tick work; the values keep updating
        // and one repaint catches up when shown again (see SetRenderingActive)
        // active by default, so an ungated graph (the sidebar mini graphs) just renders
        private bool _isRenderingActive = true;
        private ObservableCollection<double?> _boundValues;
        private bool _isValuesSubscribed;

        // in a live visual tree; only tells a permanent removal from a transient Unloaded/Loaded
        // cycle, see OnControlUnloaded
        private bool _isInLiveTree;

        // what _lineSeries points at while detached; an inert empty list, so nothing redraws off-screen
        private readonly ObservableCollection<double?> _detachedValues = new();

        // rendering graphs across every window, for the title bar status readout; (written on the UI thread, read from
        // a timer thread, a stale read for one tick is harmless)
        private static int _activeRenderingCount;
        public static int ActiveRenderingCount => _activeRenderingCount;


        // === constructor ===

        public SensorGraphControl()
        {
            InitializeComponent();

            // rendering by default, so counted right away
            _activeRenderingCount++;

            // the two series; stepline by default (see ApplyLineStyle)
            _stepSeries = new StepLineSeries<double?>
            {
                Values = new ObservableCollection<double?>(),
                GeometrySize = 0,
                DataPadding = new LvcPoint(0, 0)
            };
            _smoothSeries = new LineSeries<double?>
            {
                Values = new ObservableCollection<double?>(),
                GeometrySize = 0,
                DataPadding = new LvcPoint(0, 0),
                LineSmoothness = 1.00 // 0 = straight segments, 1 = most curved
            };
            _lineSeries = _stepSeries;
            Series = new ISeries[] { _lineSeries };

            // y-axis
            _yAxis = new Axis
            {
                IsVisible = false,
                MinLimit = 0,
                MaxLimit = null
            };
            YAxes = new ICartesianAxis[] { _yAxis };

            // x-axis, with the crosshair line following the pointer
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
            Loaded += OnControlLoaded;
            Unloaded += OnControlUnloaded;

            // initial visuals and threshold state
            ApplyStroke();
            RebuildSections();
            ApplyCardBackground();
            ApplyCardBorder();
        }


        // === livecharts binding surfaces ===

        // bound by the CartesianChart in SensorGraphControl.xaml
        public ISeries[] Series { get; }
        public ICartesianAxis[] XAxes { get; }
        public ICartesianAxis[] YAxes { get; }
        public RectangularSection[] Sections { get; private set; } = Array.Empty<RectangularSection>();
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

            // the previous collection, with our CollectionChanged handler
            g.DetachFromBoundValues();

            // null (the sensor does not exist on this hardware) becomes an empty collection, or the chart would keep
            // showing the previous ViewModels data
            g._boundValues = e.NewValue as ObservableCollection<double?> ?? new ObservableCollection<double?>();

            // an off-screen graph only remembers the collection until it is shown (see SetRenderingActive)
            if (g._isRenderingActive)
            {
                g.AttachToBoundValues();
                g.ApplyStroke();
            }
        }

        // every added or removed point (every AddDataPoint)
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


        // === live rendering gate ===

        // switches live rendering without destroying the graph:
        // off - detached from the live values, no LiveCharts work and no repaint per tick
        // on - rejoined, with one catch-up repaint
        public void SetRenderingActive(bool active)
        {
            if (_isRenderingActive == active) return;
            _isRenderingActive = active;
            _activeRenderingCount += active ? 1 : -1;

            if (active)
            {
                AttachToBoundValues();
                ForceRepaint(); // catches up on every missed tick
            }
            else
            {
                DetachFromBoundValues();
            }
        }

        // the live values and our repaint handler back on _lineSeries; idempotent
        private void AttachToBoundValues()
        {
            _boundValues ??= new ObservableCollection<double?>();
            _lineSeries.Values = _boundValues;

            if (!_isValuesSubscribed)
            {
                _boundValues.CollectionChanged += OnValuesCollectionChanged;
                _isValuesSubscribed = true;
            }
        }

        // the inert list on _lineSeries and our handler off the live values; they keep updating, nobody listens
        private void DetachFromBoundValues()
        {
            if (_isValuesSubscribed && _boundValues != null)
            {
                _boundValues.CollectionChanged -= OnValuesCollectionChanged;
            }
            _isValuesSubscribed = false;
            _lineSeries.Values = _detachedValues;
        }

        // swaps stepline and smooth on the same data and forces a repaint (the ApplyStroke guard would see an unchanged
        // signature and leave the new series unpainted)
        private void ApplyLineStyle()
        {
            ISeries target = LineStyle == GraphLineStyle.Smooth ? _smoothSeries : _stepSeries;
            if (ReferenceEquals(target, _lineSeries)) return;

            _lineSeries = target;
            _lineSeries.Values = _isValuesSubscribed && _boundValues != null ? _boundValues : _detachedValues;

            // set once by the xaml binding, driven from code after that (like Chart.Sections in RebuildSections)
            Chart.Series = new ISeries[] { _lineSeries };
            ForceRepaint();
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


        // DependencyProperty: LineStyle
        // stepline or smooth; the global setting, via SensorGraphViewModel.GraphLineStyle
        public GraphLineStyle LineStyle
        {
            get => (GraphLineStyle)GetValue(LineStyleProperty);
            set => SetValue(LineStyleProperty, value);
        }

        public static readonly DependencyProperty LineStyleProperty =
            DependencyProperty.Register(
                nameof(LineStyle),
                typeof(GraphLineStyle),
                typeof(SensorGraphControl),
                new PropertyMetadata(GraphLineStyle.Stepline, OnLineStyleChanged));

        private static void OnLineStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorGraphControl g) g.ApplyLineStyle();
        }


        // DependencyProperty: FillFade
        // a flat fill or one that fades towards the bottom; the global setting, via SensorGraphViewModel.GraphFillFade
        public bool FillFade
        {
            get => (bool)GetValue(FillFadeProperty);
            set => SetValue(FillFadeProperty, value);
        }

        public static readonly DependencyProperty FillFadeProperty =
            DependencyProperty.Register(
                nameof(FillFade),
                typeof(bool),
                typeof(SensorGraphControl),
                new PropertyMetadata(false, OnFillFadeChanged));

        private static void OnFillFadeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // the area under the line and the alarm zones both follow it
            if (d is SensorGraphControl g) g.ForceRepaint();
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

        // IsAutoScaled and ManualYMax both set the y-axis maximum
        private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorGraphControl g)
            {
                g._yAxis.MaxLimit = g.IsAutoScaled ? (double?)null : g.ManualYMax;
                g.ApplyStroke(); // the threshold moves relative to the range
                g.RebuildSections();
                g.ShowThresholdLabelBriefly();
            }
        }


        // DependencyProperty: SensorType
        // the raw LHM SensorType ("Clock"); only scales the threshold and hover labels to a bigger unit
        public string SensorType
        {
            get => (string)GetValue(SensorTypeProperty);
            set => SetValue(SensorTypeProperty, value);
        }

        public static readonly DependencyProperty SensorTypeProperty =
            DependencyProperty.Register(
                nameof(SensorType),
                typeof(string),
                typeof(SensorGraphControl),
                new PropertyMetadata(string.Empty, OnSensorTypeChanged));

        // refreshes the threshold label when a visible control is rebound to another sensor (a sensor switch)
        private static void OnSensorTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorGraphControl g) g.ShowThresholdLabelBriefly();
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
                // duplicate sets are ignored; (the ColorPicker TwoWay binding can round-trip into a StackOverflow)
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

        // ThresholdDirection and ThresholdColor; both need a full repaint
        private static void OnThresholdVisualsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (Equals(e.OldValue, e.NewValue)) return;

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


        // DependencyProperty: CurrentValueText
        public string CurrentValueText
        {
            get => (string)GetValue(CurrentValueTextProperty);
            set => SetValue(CurrentValueTextProperty, value);
        }

        public static readonly DependencyProperty CurrentValueTextProperty =
            DependencyProperty.Register(
                nameof(CurrentValueText),
                typeof(string),
                typeof(SensorGraphControl),
                new PropertyMetadata(string.Empty, OnCurrentValueChanged));


        // DependencyProperty: CurrentValueColor
        public Microsoft.UI.Xaml.Media.Brush CurrentValueColor
        {
            get => (Microsoft.UI.Xaml.Media.Brush)GetValue(CurrentValueColorProperty);
            set => SetValue(CurrentValueColorProperty, value);
        }

        public static readonly DependencyProperty CurrentValueColorProperty =
            DependencyProperty.Register(
                nameof(CurrentValueColor),
                typeof(Microsoft.UI.Xaml.Media.Brush),
                typeof(SensorGraphControl),
                new PropertyMetadata(null, OnCurrentValueColorChanged));


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


        // DependencyProperty: IsNameVisible
        public bool IsNameVisible
        {
            get => (bool)GetValue(IsNameVisibleProperty);
            set => SetValue(IsNameVisibleProperty, value);
        }

        public static readonly DependencyProperty IsNameVisibleProperty =
            DependencyProperty.Register(
                nameof(IsNameVisible),
                typeof(bool),
                typeof(SensorGraphControl),
                new PropertyMetadata(true, OnLabelChanged));


        // DependencyProperty: IsCurrentValueVisible
        public bool IsCurrentValueVisible
        {
            get => (bool)GetValue(IsCurrentValueVisibleProperty);
            set => SetValue(IsCurrentValueVisibleProperty, value);
        }

        public static readonly DependencyProperty IsCurrentValueVisibleProperty =
            DependencyProperty.Register(
                nameof(IsCurrentValueVisible),
                typeof(bool),
                typeof(SensorGraphControl),
                new PropertyMetadata(true, OnLabelChanged));

        // LabelText, CurrentValueText, IsLabelVisible, IsNameVisible, IsCurrentValueVisible
        private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SensorGraphControl g) return;

            g.GraphLabelText.Text = g.LabelText ?? string.Empty;
            g.GraphLabelText.Visibility = g.IsLabelVisible && g.IsNameVisible && !string.IsNullOrEmpty(g.LabelText)
                ? Visibility.Visible
                : Visibility.Collapsed;

            g.GraphCurrentValueText.Text = g.CurrentValueText ?? string.Empty;
            g.GraphCurrentValueText.Visibility = g.IsLabelVisible && g.IsCurrentValueVisible && !string.IsNullOrEmpty(g.CurrentValueText)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static void OnCurrentValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SensorGraphControl g) return;

            g.GraphCurrentValueText.Text = g.CurrentValueText ?? string.Empty;
            g.GraphCurrentValueText.Visibility = g.IsLabelVisible && g.IsCurrentValueVisible && !string.IsNullOrEmpty(g.CurrentValueText)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static void OnCurrentValueColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SensorGraphControl g) return;

            if (g.CurrentValueColor != null)
            {
                g.GraphCurrentValueText.Foreground = g.CurrentValueColor;
            }
        }


        // DependencyProperty: CardBackgroundOverride
        // null = the themed VisualState (CardBackgroundVisible, theme-reactive through ThemeResource brushes)
        // any color, transparent included, goes on as a plain brush past the VisualState; (the selected sidebar item,
        // or no card at all, see SensorPanelControl.ShowGraphCardBackground)
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

        // for the constructor and the property callback
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


        // DependencyProperty: CardBorderOverride
        // null = the themed VisualState (ControlStrokeColorSecondaryBrush), a color goes on as a plain brush
        public Windows.UI.Color? CardBorderOverride
        {
            get => (Windows.UI.Color?)GetValue(CardBorderOverrideProperty);
            set => SetValue(CardBorderOverrideProperty, value);
        }

        public static readonly DependencyProperty CardBorderOverrideProperty =
            DependencyProperty.Register(
                nameof(CardBorderOverride),
                typeof(Windows.UI.Color?),
                typeof(SensorGraphControl),
                new PropertyMetadata(null, OnCardBorderOverrideChanged));

        private static void OnCardBorderOverrideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorGraphControl g) g.ApplyCardBorder();
        }

        private void ApplyCardBorder()
        {
            if (CardBorderOverride is Windows.UI.Color color)
            {
                CardBorder.BorderBrush = new SolidColorBrush(color);
            }
            else
            {
                VisualStateManager.GoToState(this, "CardBackgroundVisible", false);
            }
        }


        // DependencyProperty: IsHoverEnabled
        // false unsubscribes the hover handlers (circle and value label) entirely
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
                g.HideHoverElements(); // leftover hover state
            }

            // the LiveCharts crosshair stops tracking too
            g._xAxis.CrosshairPaint = enabled ? g._crosshairPaint : null;

            // without hover the chart takes no pointer input, clicks pass through
            g.Chart.IsHitTestVisible = enabled;
        }


        // === event handlers ===

        // idea: fired once LiveCharts has drawn its first real frame, so MainWindow could prewarm
        // SkiaSharp during the splash
        //public event EventHandler ChartReady;

        // a full repaint on every entry into the live tree, so the native surface stays in
        // sync with the ApplyStroke guard
        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            _isInLiveTree = true;
            ForceRepaint();
        }

        // a permanent removal (unpinning a widget sensor destroys its container) or a transient cycle
        // (a NavigationCacheMode page leaving and coming back); deferred one tick, a transient cycle
        // has re-fired Loaded by then
        // a removal has to leave the gate, or ActiveRenderingCount overcounts for good
        private void OnControlUnloaded(object sender, RoutedEventArgs e)
        {
            _isInLiveTree = false;

            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_isInLiveTree) return; // transient
                SetRenderingActive(false);
            });
        }

        // LiveCharts builds its draw context on the first real measure pass; UpdateStarted
        // marks that (Loaded is too early)
        private void Chart_UpdateStarted(LiveChartsCore.Kernel.Sketches.IChartView chart)
        {
            Chart.UpdateStarted -= Chart_UpdateStarted;
            _isLoaded = true;
            ShowThresholdLabelBriefly();
        }
    }
}

