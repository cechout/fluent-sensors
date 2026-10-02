using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;


namespace FluentSensors.Controls.SensorRow
{
    public sealed partial class SensorRowControl : UserControl
    {
        // === fields ===

        private bool _isHovered = false;
        private bool _isPressed = false;
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

        // follows the new ViewModel, so the card reacts when IsDisabled flips on screen
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

            // --- workaround: GoToState crash during ItemsRepeater materialization ---
            // problem: this can fire while ItemsRepeater materializes the control, before it has a live XamlRoot;
            // GoToState then fails to resolve the ThemeDictionaries and throws unhandled, taking the process down
            // (found by debugging, no public report)
            // fix: skipped until loaded, OnLoaded applies the same state
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
            // selection changes from outside (SelectPinnedSensors, DeselectAllSensors) bypass RootGrid_Tapped
            else if (e.PropertyName == nameof(SensorRowViewModel.IsSelected))
            {
                UpdateVisualState();

                // null unless an automation client asked for this row
                if (FrameworkElementAutomationPeer.FromElement(this) is SensorRowAutomationPeer peer)
                {
                    peer.RaiseToggleStateChanged(ViewModel?.IsSelected == true);
                }
            }
        }

        // the hidden sensors window; (the name only, hidden sensors never update)
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

        // added to the tree, fresh or recycled
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isHovered = false;
            _isPressed = false;

            // re-attaches what Unloaded detached; a recycle with the same ViewModel never fires OnViewModelChanged
            if (ViewModel != null && !_isSubscribed)
            {
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
                _isSubscribed = true;
            }
            this.Bindings.Update();

            // no transitions on the initial state; (fast collapse and expand cycles can leave
            // the card blank mid animation)
            UpdateVisualState(useTransitions: false);
            UpdateDisplayState();
        }

        // pulled out by the ItemsRepeater; a pointer still over it never fires PointerExited, so the flags are
        // dropped here before a recycle
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _isHovered = false;
            _isPressed = false;

            // the ViewModel belongs to the SensorsViewModel singleton; our handler and the x:Bind bindings on its
            // PropertyChanged would keep every control ever created alive, native tree included
            if (ViewModel != null && _isSubscribed)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _isSubscribed = false;
            }
            this.Bindings.StopTracking();
        }


        // === event handlers ===

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

            // the clicked row takes the focus (no rectangle), so the arrow keys carry on from here
            Focus(FocusState.Pointer);
        }

        private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isPressed = false;
            UpdateVisualState();
        }

        // toggles the sensor; (a disabled card cannot be selected)
        private void RootGrid_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ToggleSelection();
        }


        // === keyboard and screen reader ===

        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new SensorRowAutomationPeer(this);
        }

        // space and enter toggle like a click, while the row itself has focus (on the badge they are the badge keys)
        // up and down belong to the list; right steps into the badge, left back out
        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, this))
            {
                if (e.Key == VirtualKey.Space || e.Key == VirtualKey.Enter)
                {
                    // once per press, a held key repeats KeyDown
                    if (!e.KeyStatus.WasKeyDown) ToggleSelection();
                    e.Handled = true;
                    return;
                }

                if (e.Key == VirtualKey.Right && IsBadgeReachable && ThresholdIndicator.Focus(FocusState.Keyboard))
                {
                    e.Handled = true;
                    return;
                }
            }
            else if (ReferenceEquals(e.OriginalSource, ThresholdIndicator))
            {
                if (e.Key == VirtualKey.Left && Focus(FocusState.Keyboard))
                {
                    e.Handled = true;
                    return;
                }

                if ((e.Key == VirtualKey.Up || e.Key == VirtualKey.Down) && TryFocusNeighbourBadge(e.Key))
                {
                    e.Handled = true;
                    return;
                }
            }

            base.OnKeyDown(e);
        }

        // up and down on a badge go to the neighbouring badge, so the threshold column walks like a table column (plain
        // navigation would land on the row)
        private bool TryFocusNeighbourBadge(VirtualKey key)
        {
            if (XamlRoot == null) return false;

            var direction = key == VirtualKey.Up ? FocusNavigationDirection.Up : FocusNavigationDirection.Down;
            var options = new FindNextElementOptions { SearchRoot = XamlRoot.Content };

            return FocusManager.FindNextElement(direction, options) is SensorRowControl neighbour
                && neighbour.IsBadgeReachable
                && neighbour.ThresholdIndicator.Focus(FocusState.Keyboard);
        }

        // no badge in compact mode
        private bool IsBadgeReachable => ThresholdIndicator.Visibility == Visibility.Visible && ThresholdIndicator.IsTabStop;

        // click, keyboard and screen reader
        internal void ToggleSelection()
        {
            if (ViewModel == null || ViewModel.IsDisabled) return;

            ViewModel.IsSelected = !ViewModel.IsSelected;
            UpdateVisualState();
        }


        // === private helpers ===

        // normal, hover, pressed and their checked variants
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

        // full details, disabled (dimmed) or name only (HiddenSensorsWindow); (the name-only columns collapse in code,
        // widths are more than setters)
        private void UpdateDisplayState()
        {
            // a disabled row ignores clicks and leaves the tab order
            IsTabStop = ViewModel?.IsDisabled != true;

            if (IsCompact)
            {
                // (SensorRowHeaderControl.UpdateColumns collapses the labels above the rows the same way)
                CurrentValueText.Visibility = Visibility.Collapsed;
                ThresholdIndicator.Visibility = Visibility.Collapsed;
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

                // the name column makes room for the unit column
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
