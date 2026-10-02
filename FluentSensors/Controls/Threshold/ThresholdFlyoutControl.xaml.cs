using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using FluentSensors.Common.Sensors;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Controls.Threshold
{
    // the threshold badge:
    // a badge with its editor flyout, shared by SensorRowControl and SensorPanelControl; it derives text and color from
    // the Threshold view model itself
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

        // follows the new Threshold and points the flyout bindings at it; a recycle onto the same reference never
        // fires this, OnLoaded covers that
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

        // re-attaches after a recycle (OnThresholdChanged skips the same reference) and refreshes the badge for
        // changes made while unloaded
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (Threshold != null && !_isThresholdSubscribed)
            {
                Threshold.PropertyChanged += Threshold_PropertyChanged;
                _isThresholdSubscribed = true;
            }

            // the badge scales its value, so a data unit switch redraws it
            SettingsService.Instance.DataUnitBasisChanged += UpdateIndicator;

            UpdateIndicator();
        }

        // Threshold belongs to a longer-lived SensorGraphViewModel or SensorRowViewModel; without the detach its
        // PropertyChanged keeps every instance alive
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (Threshold != null && _isThresholdSubscribed)
            {
                Threshold.PropertyChanged -= Threshold_PropertyChanged;
                _isThresholdSubscribed = false;
            }

            SettingsService.Instance.DataUnitBasisChanged -= UpdateIndicator;
        }


        // === public methods ===

        // for SensorPanelControl, where a tap on the graph opens the flyout too
        public void ShowFlyout()
        {
            FlyoutBase.ShowAttachedFlyout(IndicatorBorder);
        }


        // === event handlers ===

        private void IndicatorBorder_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            ShowFlyout();
        }

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

            // a tab stop badge takes the focus, so the closing flyout returns there
            if (IsTabStop) Focus(FocusState.Pointer);
        }

        private void IndicatorBorder_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isPressed = false;
            UpdateVisualState();
            e.Handled = true;
        }

        private void ThresholdCloseButton_Click(object sender, RoutedEventArgs e)
        {
            ThresholdFlyout.Hide();
        }


        // === keyboard and screen reader ===

        // a button named after the threshold; the consumer decides IsTabStop (the sensor row sets it, the graph
        // panels open through the graph)
        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new ThresholdFlyoutAutomationPeer(this);
        }

        // space and enter open the flyout like a tap
        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            if ((e.Key == VirtualKey.Space || e.Key == VirtualKey.Enter) && ReferenceEquals(e.OriginalSource, this))
            {
                if (!e.KeyStatus.WasKeyDown) ShowFlyout();
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        // the configured value, or that none is set
        internal string AutomationName => Threshold?.IsEnabled == true ? $"Threshold {IndicatorText}" : "Threshold, not set";


        // === private helpers ===

        // "-" and transparent when unset, otherwise the scaled value in the threshold color
        private void UpdateIndicator()
        {
            if (Threshold != null && Threshold.IsEnabled)
            {
                var (scaledValue, _) = SensorUnitFormatter.Scale(Threshold.Value, Threshold.SensorType);
                IndicatorText = $"{scaledValue:0}";
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
