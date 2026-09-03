using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;


namespace FluentSensors.Controls
{
    // arranges children in a single column, dividing the available height equally between them 
    public class VerticalStretchPanel : Panel
    {
        public double Spacing
        {
            get => (double)GetValue(SpacingProperty);
            set => SetValue(SpacingProperty, value);
        }

        public static readonly DependencyProperty SpacingProperty =
            DependencyProperty.Register(
                nameof(Spacing),
                typeof(double),
                typeof(VerticalStretchPanel),
                new PropertyMetadata(0.0, OnSpacingChanged));

        private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VerticalStretchPanel panel)
            {
                panel.InvalidateMeasure();
            }
        }

        // fixed height per child in DIP; 0 keeps the equal split
        // a ScrollViewer measures its content with infinite height, where an equal split has nothing to divide, so a
        // scrolling host hands the panel the row height to stack at instead
        public double FixedItemHeight
        {
            get => (double)GetValue(FixedItemHeightProperty);
            set => SetValue(FixedItemHeightProperty, value);
        }

        public static readonly DependencyProperty FixedItemHeightProperty =
            DependencyProperty.Register(
                nameof(FixedItemHeight),
                typeof(double),
                typeof(VerticalStretchPanel),
                new PropertyMetadata(0.0, OnFixedItemHeightChanged));

        private static void OnFixedItemHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VerticalStretchPanel panel)
            {
                panel.InvalidateMeasure();
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            int count = Children.Count;
            if (count == 0) return new Size(0, 0);

            // same reasoning as SquareGridPanel: a parent that measures with infinite height (e.g. a ScrollViewer) cannot
            // be handed an infinite DesiredSize back, so this is clamped to 0 here; the real, finite size arrives in
            // ArrangeOverride once the parent has resolved its actual size
            double measureWidth = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
            double measureHeight = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;

            double totalSpacing = Spacing * (count - 1);
            double cellHeight = FixedItemHeight > 0
                ? FixedItemHeight
                : Math.Max(0, (measureHeight - totalSpacing) / count);

            foreach (var child in Children)
            {
                child.Measure(new Size(measureWidth, cellHeight));
            }

            // a fixed row height is the one case with a real content height to report, and reporting it is what lets
            // a scrolling host know there is something to scroll
            return FixedItemHeight > 0
                ? new Size(measureWidth, (count * cellHeight) + totalSpacing)
                : new Size(measureWidth, measureHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            int count = Children.Count;
            if (count == 0) return finalSize;

            double totalSpacing = Spacing * (count - 1);
            double cellHeight = FixedItemHeight > 0
                ? FixedItemHeight
                : Math.Max(0, (finalSize.Height - totalSpacing) / count);

            double y = 0;
            foreach (var child in Children)
            {
                child.Arrange(new Rect(0, y, finalSize.Width, cellHeight));
                y += cellHeight + Spacing;
            }

            return finalSize;
        }
    }
}
