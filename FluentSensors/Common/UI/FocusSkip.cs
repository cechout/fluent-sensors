using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;


namespace FluentSensors.Common.UI
{
    // the focus skip:
    // a region out of keyboard navigation, the mouse still works; tab passes over it, the arrow keys stop at its edge
    // for mouse-configured content that would make the tab order endless, like the graph panels
    // and tiles of the hardware view
    public static class FocusSkip
    {
        // === attached properties ===

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(FocusSkip),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

        public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement region) return;

            region.GettingFocus -= Region_GettingFocus;
            if ((bool)e.NewValue) region.GettingFocus += Region_GettingFocus;
        }


        // === focus redirect ===

        // a click keeps its target (Direction None); only keyboard moves redirect, a tab from
        // a clicked element leaves too
        private static void Region_GettingFocus(UIElement sender, GettingFocusEventArgs args)
        {
            switch (args.Direction)
            {
                case FocusNavigationDirection.Next:
                case FocusNavigationDirection.Previous:
                    var target = FocusGroup.FindStopBeyond(sender, args.Direction == FocusNavigationDirection.Next);
                    if (target != null) args.TrySetNewFocusedElement(target);
                    break;

                case FocusNavigationDirection.Up:
                case FocusNavigationDirection.Down:
                case FocusNavigationDirection.Left:
                case FocusNavigationDirection.Right:
                    if (!FocusGroup.IsInside(args.OldFocusedElement, sender)) args.TryCancel();
                    break;
            }
        }
    }
}
