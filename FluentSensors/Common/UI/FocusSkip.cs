using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;


namespace FluentSensors.Common.UI
{
    // leaves a region out of keyboard navigation while the mouse keeps working inside it: tab and shift+tab pass over
    // it to the next stop beyond, and the arrow keys stop at its edge instead of stepping in
    //
    // for content that is configured with the mouse and would only make the tab order endless, like the graph panels
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

        // a click inside the region keeps focusing whatever it hits (Direction None); only keyboard moves are redirected,
        // and a tab from something that was clicked inside leaves the region as well
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
