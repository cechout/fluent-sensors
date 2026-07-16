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

            card.UpdateDisplayState();
        }
        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SensorRowViewModel.IsDisabled))
            {
                UpdateDisplayState();
            }
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
        private void UpdateVisualState()
        {
            if (ViewModel == null) return;

            // check status of the card (selected or not)
            bool isChecked = ViewModel.IsSelected;

            if (isChecked)
            {
                if (_isPressed) VisualStateManager.GoToState(this, "CheckedPressed", true);
                else if (_isHovered) VisualStateManager.GoToState(this, "CheckedHover", true);
                else VisualStateManager.GoToState(this, "Checked", true);
            }
            else
            {
                if (_isPressed) VisualStateManager.GoToState(this, "Pressed", true);
                else if (_isHovered) VisualStateManager.GoToState(this, "Hover", true);
                else VisualStateManager.GoToState(this, "Normal", true);
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

        public SensorCardControl()
        {
            this.InitializeComponent();

            // force the card to start immediately in the correct visual state
            this.Loaded += (s, e) =>
            {
                UpdateVisualState();
                UpdateDisplayState();
            };
        }
    }
}