using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;


namespace FluentSensors.Common.UI
{
    // the arrow navigation:
    // up and down move between rows of a settings list, left and right within one; it never leaves the list,
    // an arrow at the end stays put
    // taken in PreviewKeyDown, or a slider keeps all four arrows and a combo box up and down; left and right still
    // reach a slider, a modified key (alt+down) passes
    // a control on a SettingsExpander header sits inside the header button where no directional move reaches;
    // right steps in, left back out
    public static class ArrowNavigation
    {
        // the header toggle part of the WinUI Expander template under SettingsExpander
        private const string ExpanderHeaderPartName = "ExpanderHeader";


        // === attached properties ===

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(ArrowNavigation),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

        public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement list) return;

            list.PreviewKeyDown -= List_PreviewKeyDown;
            if ((bool)e.NewValue) list.PreviewKeyDown += List_PreviewKeyDown;
        }


        // === key handling ===

        private static void List_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (sender is not UIElement list || list.XamlRoot == null) return;

            var direction = e.Key switch
            {
                VirtualKey.Up => FocusNavigationDirection.Up,
                VirtualKey.Down => FocusNavigationDirection.Down,
                VirtualKey.Left => FocusNavigationDirection.Left,
                VirtualKey.Right => FocusNavigationDirection.Right,
                _ => FocusNavigationDirection.None
            };
            if (direction == FocusNavigationDirection.None || IsModifierDown()) return;

            // an open dropdown or flyout keeps its keys in a popup outside the list
            if (FocusManager.GetFocusedElement(list.XamlRoot) is not DependencyObject focused) return;
            if (!FocusGroup.IsInside(focused, list)) return;

            bool horizontal = direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right;
            if (horizontal && focused is Slider) return;

            if (direction == FocusNavigationDirection.Right
                && focused is ToggleButton { Name: ExpanderHeaderPartName } header
                && FocusManager.FindFirstFocusableElement(header) is Control headerControl
                && headerControl.Focus(FocusState.Keyboard))
            {
                e.Handled = true;
                return;
            }

            if (direction == FocusNavigationDirection.Left
                && FindExpanderHeader(focused) is ToggleButton owningHeader
                && !ReferenceEquals(owningHeader, focused)
                && owningHeader.Focus(FocusState.Keyboard))
            {
                e.Handled = true;
                return;
            }

            // handled even without a target, so the key never reaches the control
            FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = list });
            e.Handled = true;
        }

        private static ToggleButton? FindExpanderHeader(DependencyObject element)
        {
            for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is ToggleButton { Name: ExpanderHeaderPartName } header) return header;
            }
            return null;
        }

        private static bool IsModifierDown() =>
            IsKeyDown(VirtualKey.Menu) || IsKeyDown(VirtualKey.Control) || IsKeyDown(VirtualKey.Shift);

        private static bool IsKeyDown(VirtualKey key) =>
            InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    }
}
