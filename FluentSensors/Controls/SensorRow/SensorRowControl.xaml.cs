using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;


namespace FluentSensors.Controls.SensorRow
{
    public sealed partial class SensorRowControl : UserControl
    {
        // === fields ===

        private bool _isHovered = false;
        private bool _isPressed = false;
        private bool _isThresholdIndicatorHovered = false;
        private bool _isThresholdIndicatorPressed = false;
        private bool _isSubscribed = false;


        // === constructor ===

        public SensorRowControl()
        {
            this.InitializeComponent();

            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;
        }


        // === dependency properties ===

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(SensorRowViewModel),
                typeof(SensorRowControl),
                new PropertyMetadata(null, OnViewModelChanged));

        public SensorRowViewModel ViewModel
        {
            get => (SensorRowViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        // re-subscribes to the new ViewModels PropertyChanged so the card reacts if IsDisabled flips while its on screen
        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SensorRowControl card) return;

            if (e.OldValue is SensorRowViewModel oldVm)
            {
                oldVm.PropertyChanged -= card.ViewModel_PropertyChanged;
                card._isSubscribed = false;
            }
            if (e.NewValue is SensorRowViewModel newVm)
            {
                newVm.PropertyChanged += card.ViewModel_PropertyChanged;
                card._isSubscribed = true;
            }

            card._isHovered = false;
            card._isPressed = false;

            // guard: this callback can fire while ItemsRepeater is still materializing the control, before its attached
            // to a live XamlRoot
            // Calling VisualStateManager.GoToState that early can fail to resolve this controls own
            // ThemeDictionaries resources and throw unhandled, that crashed the whole process
            // OnLoaded (below) already applies the same state once the control is actually ready, so skipping here just
            // defers it safely
            if (card.IsLoaded)
            {
                card.UpdateVisualState(useTransitions: false);
                card.UpdateDisplayState();
            }
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SensorRowViewModel.IsDisabled))
            {
                UpdateDisplayState();
            }
            // covers external selection changes (e.g. SelectPinnedSensors / DeselectAllSensors), since those bypass
            // RootGrid_Tapped and never trigger UpdateVisualState on their own
            else if (e.PropertyName == nameof(SensorRowViewModel.IsSelected))
            {
                UpdateVisualState();
            }
        }

        // true for cards in the hidden sensors window
        // (only the name matters there, values never update for hidden sensors anyway)
        public static readonly DependencyProperty IsCompactProperty =
            DependencyProperty.Register(
                nameof(IsCompact),
                typeof(bool),
                typeof(SensorRowControl),
                new PropertyMetadata(false, OnIsCompactChanged));

        public bool IsCompact
        {
            get => (bool)GetValue(IsCompactProperty);
            set => SetValue(IsCompactProperty, value);
        }

        private static void OnIsCompactChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorRowControl card) card.UpdateDisplayState();
        }


        // === lifecycle events ===

        // container was just added to the visual tree, either fresh or recycled from another item
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isHovered = false;
            _isPressed = false;
            _isThresholdIndicatorHovered = false;
            _isThresholdIndicatorPressed = false;

            // re-attach everything that Unloaded detached; the control can come back after a recycle with the same ViewModel,
            // in which case OnViewModelChanged never fires again and would leave the card dead
            if (ViewModel != null && !_isSubscribed)
            {
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
                _isSubscribed = true;
            }
            this.Bindings.Update(); 

            // skip transitions on the initial state
            // (fast collapse/expand cycles otherwise interrupt animations mid-flight and leave the card visually blank sometimes)
            UpdateVisualState(useTransitions: false);
            UpdateDisplayState();
        }

        // container is being pulled out of the visual tree by the ItemsRepeater, if the pointer was over the card at that
        // moment, PointerExited never fires, so drop the hover/press flags manually or they will be stuck true when this
        // instance gets recycled to a different sensor row
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _isHovered = false;
            _isPressed = false;

            // memory leak fix: the ViewModel belongs to the SensorsViewModel singleton and outlives this control by far.
            // Both the manual handler below and the compiled x:Bind bindings register on the ViewModels PropertyChanged and
            // hold a strong reference back to this control - without detaching them here, every control ever created stays
            // reachable from the singleton, keeping its entire native visual tree alive
            if (ViewModel != null && _isSubscribed)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _isSubscribed = false;
            }
            this.Bindings.StopTracking(); 
        }


        // === event handlers ===

        // pointer events
        private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (ViewModel?.IsDisabled == true) return;
            _isHovered = true;
            UpdateVisualState();
        }

        private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isHovered = false;
            _isPressed = false;
            UpdateVisualState();
        }

        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (ViewModel?.IsDisabled == true) return;
            _isPressed = true;
            UpdateVisualState();
        }

        private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isPressed = false;
            UpdateVisualState();
        }

        // click event to toggle the sensor on/off - disabled cards cant be selected
        private void RootGrid_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (ViewModel == null || ViewModel.IsDisabled) return;

            ViewModel.IsSelected = !ViewModel.IsSelected;
            UpdateVisualState();
        }

        // threshold badge
        // (press state via opacity only, no VSM)
        private void ThresholdIndicatorBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _isThresholdIndicatorHovered = true;
            UpdateThresholdIndicatorVisualState();
        }

        private void ThresholdIndicatorBorder_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isThresholdIndicatorHovered = false;
            _isThresholdIndicatorPressed = false;
            UpdateThresholdIndicatorVisualState();
        }

        private void ThresholdIndicatorBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isThresholdIndicatorPressed = true;
            UpdateThresholdIndicatorVisualState();
            e.Handled = true;
        }

        private void ThresholdIndicatorBorder_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isThresholdIndicatorPressed = false;
            UpdateThresholdIndicatorVisualState();
            e.Handled = true;
        }

        private void ThresholdIndicatorBorder_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            FlyoutBase.ShowAttachedFlyout(ThresholdIndicatorBorder);
        }

        private void ThresholdCloseButton_Click(object sender, RoutedEventArgs e)
        {
            ThresholdFlyout.Hide(); 
        }


        // === private helpers ===

        // decides between normal / hover / pressed / checked-variants for the whole card, based on selection state
        private void UpdateVisualState(bool useTransitions = true)
        {
            if (ViewModel == null) return;

            bool isChecked = ViewModel.IsSelected;

            if (isChecked)
            {
                if (_isPressed) VisualStateManager.GoToState(this, "CheckedPressed", useTransitions);
                else if (_isHovered) VisualStateManager.GoToState(this, "CheckedHover", useTransitions);
                else VisualStateManager.GoToState(this, "Checked", useTransitions);
            }
            else
            {
                if (_isPressed) VisualStateManager.GoToState(this, "Pressed", useTransitions);
                else if (_isHovered) VisualStateManager.GoToState(this, "Hover", useTransitions);
                else VisualStateManager.GoToState(this, "Normal", useTransitions);
            }
        }

        // decides between normal / hover / pressed for the threshold badge
        // (hover-gray only applies when no threshold is configured yet, since a configured threshold already communicates
        // state via its own background color)
        private void UpdateThresholdIndicatorVisualState()
        {
            bool isConfigured = ViewModel?.Threshold?.IsEnabled == true;

            if (_isThresholdIndicatorPressed) { VisualStateManager.GoToState(this, isConfigured ? "IndicatorPressedConfigured" : "IndicatorPressedUnconfigured", true); }
            else if (_isThresholdIndicatorHovered && !isConfigured) { VisualStateManager.GoToState(this, "IndicatorHover", true); }
            else { VisualStateManager.GoToState(this, "IndicatorNormal", true); }
        }

        // decides between full details, disabled (dimmed/frozen), or name-only (in HiddenSensorsWindow)
        // (column collapsing for name-only happens here in code-behind rather than via VSM, since it involves width changes,
        // not just setters)
        private void UpdateDisplayState()
        {
            if (IsCompact)
            {
                CurrentValueText.Visibility = Visibility.Collapsed;
                ThresholdIndicatorBorder.Visibility = Visibility.Collapsed;
                MinimumValueText.Visibility = Visibility.Collapsed;
                MaximumValueText.Visibility = Visibility.Collapsed;
                AverageValueText.Visibility = Visibility.Collapsed;

                CurrentColumn.MinWidth = 0;
                ThresholdColumn.MinWidth = 0;
                MinimumColumn.MinWidth = 0;
                MaximumColumn.MinWidth = 0;
                AverageColumn.MinWidth = 0;

                CurrentColumn.Width = new GridLength(0);
                ThresholdColumn.Width = new GridLength(0);
                MinimumColumn.Width = new GridLength(0);
                MaximumColumn.Width = new GridLength(0);
                AverageColumn.Width = new GridLength(0);

                // name column shrinks to make room for the new unit column
                NameColumn.Width = new GridLength(3, GridUnitType.Star);
                UnitColumn.Width = new GridLength(40);
                UnitText.Visibility = Visibility.Visible;

                VisualStateManager.GoToState(this, "FullDetails", true);
                return;
            }

            VisualStateManager.GoToState(this, ViewModel?.IsDisabled == true ? "Disabled" : "FullDetails", true);
        }
    }
}