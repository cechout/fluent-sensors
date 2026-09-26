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
    // walks a settings style list with the arrow keys: up and down always move to the row above or below, left and
    // right between the controls of one row, and the search never leaves the list, so an arrow at its first or last
    // row simply stays put
    //
    // taken in PreviewKeyDown, before the focused control sees the key; a slider would otherwise keep all four arrows
    // and a combo box up and down, and focus could never move past either of them; left and right still reach a
    // slider, and a key with a modifier (alt+down opens a combo box) is left alone
    //
    // a control placed on a SettingsExpander header sits inside the header button, so no directional move can ever
    // reach it; right steps from the header into it, left back out
    public static class ArrowNavigation
    {
        // name of the header toggle part in the WinUI Expander template that SettingsExpander builds on
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

            // an open dropdown or flyout holds focus in a popup outside the list and keeps its keys
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

            // handled even when nothing lies in that direction, so the key never falls through to the control
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
