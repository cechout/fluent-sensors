using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using FluentSensors.Common.Sensors;


namespace FluentSensors.Controls.Threshold
{
    // self-contained threshold badge + editor flyout:
    // extracted from SensorRowControl so SensorPanelControl (and any future consumer) can reuse the exact same
    // compact threshold editing UI without duplicating it unlike the original SensorRowControl version, this derives
    // its own badge text/color reactively from the Threshold VM directly instead of requiring the consumer to
    // precompute and expose them
    public sealed partial class ThresholdFlyoutControl : UserControl, INotifyPropertyChanged
    {
        // === fields ===

        private bool _isHovered;
        private bool _isPressed;
        private bool _isThresholdSubscribed;


        // === constructor ===

        public ThresholdFlyoutControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }


        // === dependency properties ===

        public ThresholdEditorViewModel Threshold
        {
            get => (ThresholdEditorViewModel)GetValue(ThresholdProperty);
            set => SetValue(ThresholdProperty, value);
        }

        public static readonly DependencyProperty ThresholdProperty =
            DependencyProperty.Register(
                nameof(Threshold),
                typeof(ThresholdEditorViewModel),
                typeof(ThresholdFlyoutControl),
                new PropertyMetadata(null, OnThresholdChanged));

        // re-subscribes to the new Thresholds PropertyChanged so the badge stays in sync, and refreshes the x:Bind
        // bindings in the flyout (Threshold.Increase etc.) to point at the new instance
        // note: this only fires when the DP value actually changes; if a recycled container gets rebound to the exact
        // same Threshold reference it held before, WinUI skips this callback entirely
        // OnLoaded below is what catches that case and re-subscribes
        private static void OnThresholdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ThresholdFlyoutControl control) return;

            if (e.OldValue is ThresholdEditorViewModel oldVm && control._isThresholdSubscribed)
            {
                oldVm.PropertyChanged -= control.Threshold_PropertyChanged;
                control._isThresholdSubscribed = false;
            }
            if (e.NewValue is ThresholdEditorViewModel newVm)
            {
                newVm.PropertyChanged += control.Threshold_PropertyChanged;
                control._isThresholdSubscribed = true;
            }

            control.Bindings.Update();
            control.UpdateIndicator();
        }


        // === bindable properties ===

        private string _indicatorText = "-";
        public string IndicatorText
        {
            get => _indicatorText;
            private set { _indicatorText = value; OnPropertyChanged(); }
        }

        private Brush _indicatorBrush = new SolidColorBrush(Colors.Transparent);
        public Brush IndicatorBrush
        {
            get => _indicatorBrush;
            private set { _indicatorBrush = value; OnPropertyChanged(); }
        }


        // === lifecycle events ===

        // re-attaches the subscription after this control comes back from being pulled out of the visual tree
        // (SettingsExpander/ItemsRepeater container recycling):
        // if it gets rebound to the same Threshold reference it already had, OnThresholdChanged never fires again
        // (old == new), so this is the only place that reliably re-establishes it; also refreshes the badge in case
        // the threshold changed elsewhere while unloaded
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (Threshold != null && !_isThresholdSubscribed)
            {
                Threshold.PropertyChanged += Threshold_PropertyChanged;
                _isThresholdSubscribed = true;
            }

            UpdateIndicator();
        }

        // memory leak fix:
        // Threshold is owned by the parent SensorGraphViewModel/SensorRowViewModel, which can outlive this control
        // across recycling; without detaching here every instance ever created would stay reachable through the
        // thresholds PropertyChanged event
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (Threshold != null && _isThresholdSubscribed)
            {
                Threshold.PropertyChanged -= Threshold_PropertyChanged;
                _isThresholdSubscribed = false;
            }
        }


        // === event handlers ===

        private void Threshold_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ThresholdEditorViewModel.IsEnabled)
                or nameof(ThresholdEditorViewModel.Value)
                or nameof(ThresholdEditorViewModel.Color))
            {
                UpdateIndicator();
            }
        }

        private void IndicatorBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _isHovered = true;
            UpdateVisualState();
        }

        private void IndicatorBorder_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isHovered = false;
            _isPressed = false;
            UpdateVisualState();
        }

        private void IndicatorBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isPressed = true;
            UpdateVisualState();
            e.Handled = true;
        }

        private void IndicatorBorder_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isPressed = false;
            UpdateVisualState();
            e.Handled = true;
        }

        private void IndicatorBorder_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            FlyoutBase.ShowAttachedFlyout(IndicatorBorder);
        }

        private void ThresholdCloseButton_Click(object sender, RoutedEventArgs e)
        {
            ThresholdFlyout.Hide();
        }


        // === private helpers ===

        // recomputes badge text/color: "-" and transparent when unconfigured, otherwise the value and the
        // threshold's own color
        private void UpdateIndicator()
        {
            if (Threshold != null && Threshold.IsEnabled)
            {
                IndicatorText = $"{Threshold.Value:0}";
                IndicatorBrush = Threshold.ColorBrush;
            }
            else
            {
                IndicatorText = "-";
                IndicatorBrush = new SolidColorBrush(Colors.Transparent);
            }
        }

        private void UpdateVisualState()
        {
            bool isConfigured = Threshold?.IsEnabled == true;

            if (_isPressed) VisualStateManager.GoToState(this, isConfigured ? "IndicatorPressedConfigured" : "IndicatorPressedUnconfigured", true);
            else if (_isHovered && !isConfigured) VisualStateManager.GoToState(this, "IndicatorHover", true);
            else VisualStateManager.GoToState(this, "IndicatorNormal", true);
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}