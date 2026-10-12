using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;


namespace FluentSensors.Controls
{
    // two children in one row: the first takes what it needs and is cut when the row gets too narrow, the second keeps
    // its size and sits right behind it (a title that trims, with an icon that stays)
    // a Grid cannot do both: an Auto column never trims, a star column pushes the second child to the far end
    public class TrailingContentPanel : Panel
    {
        // gap between the two; dropped while the second child is collapsed
        public double Spacing
        {
            get => (double)GetValue(SpacingProperty);
            set => SetValue(SpacingProperty, value);
        }

        public static readonly DependencyProperty SpacingProperty =
            DependencyProperty.Register(
                nameof(Spacing),
                typeof(double),
                typeof(TrailingContentPanel),
                new PropertyMetadata(0.0, OnSpacingChanged));

        private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TrailingContentPanel panel)
            {
                panel.InvalidateMeasure();
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (Children.Count == 0) return new Size(0, 0);

            var first = Children[0];
            double trailingWidth = 0;
            double height = 0;

            // the trailing child first, the rest of the width is what the first one may use
            if (Children.Count > 1)
            {
                var trailing = Children[1];
                trailing.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                trailingWidth = GetTrailingWidth(trailing);
                height = trailing.DesiredSize.Height;
            }

            first.Measure(new Size(Math.Max(0, availableSize.Width - trailingWidth), availableSize.Height));

            return new Size(first.DesiredSize.Width + trailingWidth, Math.Max(height, first.DesiredSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Children.Count == 0) return finalSize;

            var first = Children[0];
            var trailing = Children.Count > 1 ? Children[1] : null;
            double trailingWidth = trailing != null ? GetTrailingWidth(trailing) : 0;

            double firstWidth = Math.Min(first.DesiredSize.Width, Math.Max(0, finalSize.Width - trailingWidth));
            first.Arrange(new Rect(0, 0, firstWidth, finalSize.Height));

            if (trailing != null)
            {
                double x = firstWidth + trailingWidth - trailing.DesiredSize.Width;
                trailing.Arrange(new Rect(x, 0, trailing.DesiredSize.Width, finalSize.Height));
            }

            return finalSize;
        }

        // its width plus the gap in front of it; nothing while it is collapsed
        private double GetTrailingWidth(UIElement trailing) =>
            trailing.DesiredSize.Width > 0 ? trailing.DesiredSize.Width + Spacing : 0;
    }
}
