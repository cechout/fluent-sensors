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
            double cellHeight = Math.Max(0, (measureHeight - totalSpacing) / count);

            foreach (var child in Children)
            {
                child.Measure(new Size(measureWidth, cellHeight));
            }

            return new Size(measureWidth, measureHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            int count = Children.Count;
            if (count == 0) return finalSize;

            double totalSpacing = Spacing * (count - 1);
            double cellHeight = Math.Max(0, (finalSize.Height - totalSpacing) / count);

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