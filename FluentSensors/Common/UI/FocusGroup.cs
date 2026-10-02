using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;


namespace FluentSensors.Common.UI
{
    // the focus group:
    // a container as one tab stop with the arrow keys inside, like File Explorer and Task Manager; tab jumps between
    // regions, never item by item
    // tab and shift+tab enter at the first element and a tab inside always leaves; enforced here, since
    // TabFocusNavigation=Once still walked SettingsExpander rows
    // only where the controls leave the arrows alone (a slider or combo box swallows them, see ArrowNavigation)
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

                // never a stop itself; an ItemsControl would take the entry and show no focus rectangle
                if (group is Control control) control.IsTabStop = false;
            }
            else
            {
                group.ClearValue(UIElement.TabFocusNavigationProperty);
                group.ClearValue(UIElement.XYFocusKeyboardNavigationProperty);
            }
        }


        // === tab handling ===

        // GettingFocus bubbles, so this sees every move into the group; only tab and shift+tab are redirected
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

        // the next (or previous) stop outside region in visual tree order (no TabIndex anywhere), wrapping like tab
        // a skipped region is passed over, a group entered at its first element
        internal static DependencyObject? FindStopBeyond(DependencyObject region, bool forward)
        {
            DependencyObject from = region;

            // bounded, a window of only skipped regions must not loop forever
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

        // a control precedes its children in tab order, first going forward and last going back
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
