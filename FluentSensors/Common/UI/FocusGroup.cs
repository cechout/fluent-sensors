using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;


namespace FluentSensors.Common.UI
{
    // turns a container into one stop in the tab order, with the arrow keys moving between everything inside it; the
    // same split File Explorer and Task Manager use, so tab jumps between whole regions (the menu, a command bar, one
    // section of a list) and never walks a long list item by item
    //
    // tab and shift+tab both enter a group at its first element, and a tab pressed inside a group always leaves it;
    // TabFocusNavigation=Once alone promises the same, but inside the SettingsExpander groups a tab still walked row by
    // row, so the group enforces both itself
    //
    // only for regions whose controls leave the arrow keys alone; a slider or a combo box inside a group would swallow
    // the arrows, see ArrowNavigation for the settings page, which has both
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

                // a group is only ever a container, never a stop of its own; an ItemsControl would otherwise take the
                // entry itself and show no focus rectangle anywhere
                if (group is Control control) control.IsTabStop = false;
            }
            else
            {
                group.ClearValue(UIElement.TabFocusNavigationProperty);
                group.ClearValue(UIElement.XYFocusKeyboardNavigationProperty);
            }
        }


        // === tab handling ===

        // GettingFocus bubbles, so this sees every focus change that lands inside the group; only tab and shift+tab
        // are touched, arrow keys, clicks and code keep their target
        private static void Group_GettingFocus(UIElement sender, GettingFocusEventArgs args)
        {
            bool forward = args.Direction == FocusNavigationDirection.Next;
            if (!forward && args.Direction != FocusNavigationDirection.Previous) return;

            // with one group inside another, the inner one owns the move
            if (HasGroupBetween(args.NewFocusedElement, sender)) return;

            DependencyObject? target = IsInside(args.OldFocusedElement, sender)
                ? FindStopBeyond(sender, forward)
                : FocusManager.FindFirstFocusableElement(sender);

            if (target != null && !ReferenceEquals(target, args.NewFocusedElement))
            {
                args.TrySetNewFocusedElement(target);
            }
        }


        // === tab order ===

        // the next (or previous) stop outside region, in the order tab walks: the visual tree order, since nothing in
        // this app sets a TabIndex; wraps around the end of the window the way tab does
        // the stop found is adjusted to the rules above: a skipped region is passed over, a group is entered at its
        // first element
        internal static DependencyObject? FindStopBeyond(DependencyObject region, bool forward)
        {
            DependencyObject from = region;

            // bounded, a window full of nothing but skipped regions must not loop forever
            for (int hop = 0; hop < 32; hop++)
            {
                var stop = FindNextInTree(from, forward);
                if (stop == null) return null;

                var skipped = FindAncestor(stop, FocusSkip.GetIsEnabled);
                if (skipped != null)
                {
                    from = skipped;
                    continue;
                }

                var group = FindAncestor(stop, GetIsEnabled);
                return group != null && !ReferenceEquals(group, region)
                    ? FocusManager.FindFirstFocusableElement(group) ?? stop
                    : stop;
            }
            return null;
        }

        private static DependencyObject? FindNextInTree(DependencyObject from, bool forward)
        {
            var root = (from as UIElement)?.XamlRoot?.Content;
            int step = forward ? 1 : -1;

            for (var current = from; !ReferenceEquals(current, root);)
            {
                var parent = VisualTreeHelper.GetParent(current);
                if (parent == null) break;

                int count = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = IndexOfChild(parent, current, count) + step; i >= 0 && i < count; i += step)
                {
                    var stop = FindStopWithin(VisualTreeHelper.GetChild(parent, i), forward);
                    if (stop != null) return stop;
                }
                current = parent;
            }

            return root != null ? FindStopWithin(root, forward) : null;
        }

        // a focusable control comes before its own children in tab order, so it is checked first going forward and
        // last going back
        private static DependencyObject? FindStopWithin(DependencyObject scope, bool forward)
        {
            if (scope is UIElement { Visibility: Visibility.Collapsed }) return null;
            if (forward && IsStop(scope)) return scope;

            var inner = forward
                ? FocusManager.FindFirstFocusableElement(scope)
                : FocusManager.FindLastFocusableElement(scope);
            if (inner != null) return inner;

            return !forward && IsStop(scope) ? scope : null;
        }

        private static bool IsStop(DependencyObject element) =>
            element is Control { IsTabStop: true, IsEnabled: true, Visibility: Visibility.Visible };

        private static int IndexOfChild(DependencyObject parent, DependencyObject child, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(VisualTreeHelper.GetChild(parent, i), child)) return i;
            }
            return count;
        }

        internal static bool IsInside(DependencyObject? element, DependencyObject region) =>
            FindAncestor(element, d => ReferenceEquals(d, region)) != null;

        private static DependencyObject? FindAncestor(DependencyObject? element, Func<DependencyObject, bool> match)
        {
            for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (match(current)) return current;
            }
            return null;
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
