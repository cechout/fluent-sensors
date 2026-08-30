using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;


namespace FluentSensors.Controls
{
    // arranges children in a single row, dividing the available width equally between them
    public class HorizontalStretchPanel : Panel
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
                typeof(HorizontalStretchPanel),
                new PropertyMetadata(0.0, OnSpacingChanged));

        private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HorizontalStretchPanel panel)
            {
                panel.InvalidateMeasure();
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            int count = Children.Count;
            if (count == 0) return new Size(0, 0);

            double measureWidth = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
            double measureHeight = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;

            double totalSpacing = Spacing * (count - 1);
            double cellWidth = Math.Max(0, (measureWidth - totalSpacing) / count);

            foreach (var child in Children)
            {
                child.Measure(new Size(cellWidth, measureHeight));
            }

            return new Size(measureWidth, measureHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            int count = Children.Count;
            if (count == 0) return finalSize;

            double totalSpacing = Spacing * (count - 1);
            double cellWidth = Math.Max(0, (finalSize.Width - totalSpacing) / count);

            double x = 0;
            foreach (var child in Children)
            {
                child.Arrange(new Rect(x, 0, cellWidth, finalSize.Height));
                x += cellWidth + Spacing;
            }

            return finalSize;
        }
    }
}
