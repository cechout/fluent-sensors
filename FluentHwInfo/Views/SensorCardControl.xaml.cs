using System.ComponentModel;
using FluentHwInfo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FluentHwInfo.Views
{
    public sealed partial class SensorCardControl : UserControl
    {
        // mouse tracker fields
        private bool _isHovered = false;
        private bool _isPressed = false;

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(SensorRowViewModel),
                typeof(SensorCardControl),
                new PropertyMetadata(null, OnViewModelChanged));

        public SensorRowViewModel ViewModel
        {
            get => (SensorRowViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        // true for cards in the hidden sensors window
        // (only the name matters there, values never update for hidden sensors anyway)
        public static readonly DependencyProperty IsCompactProperty =
            DependencyProperty.Register(
                nameof(IsCompact),
                typeof(bool),
                typeof(SensorCardControl),
                new PropertyMetadata(false, OnIsCompactChanged));

        public bool IsCompact
        {
            get => (bool)GetValue(IsCompactProperty);
            set => SetValue(IsCompactProperty, value);
        }

        private static void OnIsCompactChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorCardControl card) card.UpdateDisplayState();
        }


        // re-subscribes to the new ViewModels PropertyChanged so the card reacts if IsDisabled flips while its on screen
        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SensorCardControl card) return;

            if (e.OldValue is SensorRowViewModel oldVm) oldVm.PropertyChanged -= card.ViewModel_PropertyChanged;
            if (e.NewValue is SensorRowViewModel newVm) newVm.PropertyChanged += card.ViewModel_PropertyChanged;

            // ItemsRepeater may have recycled this instance from a different row - drop any stale hover/press state and snap directly into the new row's state
            card._isHovered = false;
            card._isPressed = false;
            card.UpdateVisualState(useTransitions: false);
            card.UpdateDisplayState();
        }
        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SensorRowViewModel.IsDisabled))
            {
                UpdateDisplayState();
            }
        }


        // constructor
        public SensorCardControl()
        {
            this.InitializeComponent();

            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;
        }


        // container was just added to the visual tree, either fresh or recycled from another item
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isHovered = false;
            _isPressed = false;

            // skip transitions on the initial state - fast collapse/expand cycles otherwise interrupt animations mid-flight and leave the card visually blank
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
        }


        // pointer events
        // (disabled cards just ignore hover/press)
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

        // click event to toggle the sensor on/off - disabled cards can't be selected
        private void RootGrid_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (ViewModel == null || ViewModel.IsDisabled) return;

            ViewModel.IsSelected = !ViewModel.IsSelected;
            UpdateVisualState();
        }


        // method to update the visual state of the card based on the current status (selected, hovered, pressed)
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


        // decides between full details, disabled (dimmed/frozen), or name-only (hidden window)
        private void UpdateDisplayState()
        {
            if (IsCompact)
            {
                CurrentValueText.Visibility = Visibility.Collapsed;
                MinimumValueText.Visibility = Visibility.Collapsed;
                MaximumValueText.Visibility = Visibility.Collapsed;
                AverageValueText.Visibility = Visibility.Collapsed;

                CurrentColumn.MinWidth = 0;
                MinimumColumn.MinWidth = 0;
                MaximumColumn.MinWidth = 0;
                AverageColumn.MinWidth = 0;

                CurrentColumn.Width = new GridLength(0);
                MinimumColumn.Width = new GridLength(0);
                MaximumColumn.Width = new GridLength(0);
                AverageColumn.Width = new GridLength(0);
                NameColumn.Width = new GridLength(1, GridUnitType.Star);

                VisualStateManager.GoToState(this, "FullDetails", true);
                return;
            }

            VisualStateManager.GoToState(this, ViewModel?.IsDisabled == true ? "Disabled" : "FullDetails", true);
        }
    }
}