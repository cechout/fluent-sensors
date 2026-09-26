using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;


namespace FluentSensors.Common.UI
{
    // turns a container into one stop in the tab order, with the arrow keys moving between everything inside it; the
    // same split File Explorer and Task Manager use, so tab jumps between whole regions (the menu, a command bar, one
    // section of a list) and never walks a long list item by item
    //
    // tab and shift+tab both enter a group at its first element; left alone, WinUI enters a group reached with
    // shift+tab at its last element, which lands somewhere in the middle of a section instead of on its header
    //
    // only for regions whose controls leave the arrow keys alone; a slider, a text box or a combo box inside a group
    // would swallow the arrows, and everything behind it in the group could no longer be reached
    public static class FocusGroup
    {
        // === attached properties ===

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(FocusGroup),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

        public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement group) return;

            group.GettingFocus -= Group_GettingFocus;

            if ((bool)e.NewValue)
            {
                group.TabFocusNavigation = KeyboardNavigationMode.Once;
                group.XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled;
                group.GettingFocus += Group_GettingFocus;
            }
            else
            {
                group.ClearValue(UIElement.TabFocusNavigationProperty);
                group.ClearValue(UIElement.XYFocusKeyboardNavigationProperty);
            }
        }


        // === group entry ===

        // GettingFocus bubbles, so this sees every focus change that lands inside the group; only a tab or shift+tab
        // coming from outside is redirected, arrow keys, clicks and code keep their target
        private static void Group_GettingFocus(UIElement sender, GettingFocusEventArgs args)
        {
            if (args.InputDevice != FocusInputDeviceKind.Keyboard) return;
            if (args.Direction != FocusNavigationDirection.Next && args.Direction != FocusNavigationDirection.Previous) return;
            if (IsInside(args.OldFocusedElement, sender)) return;

            // with one group inside another, the inner one owns the entry
            if (HasGroupBetween(args.NewFocusedElement, sender)) return;

            var first = FocusManager.FindFirstFocusableElement(sender);
            if (first != null && !ReferenceEquals(first, args.NewFocusedElement))
            {
                args.TrySetNewFocusedElement(first);
            }
        }

        private static bool IsInside(DependencyObject? element, DependencyObject group)
        {
            for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (ReferenceEquals(current, group)) return true;
            }
            return false;
        }

        private static bool HasGroupBetween(DependencyObject? element, DependencyObject group)
        {
            for (var current = element; current != null && !ReferenceEquals(current, group); current = VisualTreeHelper.GetParent(current))
            {
                if (GetIsEnabled(current)) return true;
            }
            return false;
        }
    }
}
