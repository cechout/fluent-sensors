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
                new PropertyMetadata(null));

        public SensorRowViewModel ViewModel
        {
            get => (SensorRowViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        // pointer events (hover and pressed states)
        private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
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
            _isPressed = true;
            UpdateVisualState();
        }

        private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isPressed = false;
            UpdateVisualState();
        }

        // click event to toggle the sensor on/off
        private void RootGrid_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.IsSelected = !ViewModel.IsSelected;
                UpdateVisualState();
            }
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

        public SensorCardControl()
        {
            this.InitializeComponent();

            // we force the card to start immediately in the correct visual state
            this.Loaded += (s, e) => UpdateVisualState();
        }
    }
}