using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;


namespace FluentSensors.Controls
{
    // one column, the height split equally
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

        // per child in DIP, 0 keeps the equal split; a ScrollViewer measures with infinite height,
        // so a scrolling host sets one
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

            // an infinite DesiredSize is invalid, so infinity (a ScrollViewer) measures as 0; the real
            // size comes in ArrangeOverride
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

            // only a fixed row height has a content height, which tells a scrolling host there is something to scroll
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
