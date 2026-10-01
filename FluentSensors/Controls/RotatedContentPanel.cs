using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;


namespace FluentSensors.Controls
{
    // turns its single child by quarter turns and lays it out in the turned space, so a child turned by 90 degrees is
    // measured with width and height swapped and still fills the panel exactly
    //
    // WinUI has no LayoutTransform; a RenderTransform alone turns the pixels but leaves the layout slot unturned, so
    // the child would be sized for the wrong shape and then stick out of the panel
    public class RotatedContentPanel : Panel
    {
        // clockwise quarter turns: 0 none, 1 = 90 degrees clockwise, 2 = 180 degrees, 3 = 90 degrees counterclockwise
        public int QuarterTurns
        {
            get => (int)GetValue(QuarterTurnsProperty);
            set => SetValue(QuarterTurnsProperty, value);
        }

        public static readonly DependencyProperty QuarterTurnsProperty =
            DependencyProperty.Register(
                nameof(QuarterTurns),
                typeof(int),
                typeof(RotatedContentPanel),
                new PropertyMetadata(0, OnTransformChanged));

        // mirrors the turned child left to right
        public bool IsMirrored
        {
            get => (bool)GetValue(IsMirroredProperty);
            set => SetValue(IsMirroredProperty, value);
        }

        public static readonly DependencyProperty IsMirroredProperty =
            DependencyProperty.Register(
                nameof(IsMirrored),
                typeof(bool),
                typeof(RotatedContentPanel),
                new PropertyMetadata(false, OnTransformChanged));

        private static void OnTransformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RotatedContentPanel panel)
            {
                panel.InvalidateMeasure();
            }
        }

        private int NormalizedTurns => ((QuarterTurns % 4) + 4) % 4;

        private bool IsSideways => NormalizedTurns % 2 == 1;

        protected override Size MeasureOverride(Size availableSize)
        {
            if (Children.Count == 0) return new Size(0, 0);

            var child = Children[0];

            if (!IsSideways)
            {
                child.Measure(availableSize);
                return child.DesiredSize;
            }

            child.Measure(new Size(availableSize.Height, availableSize.Width));
            return new Size(child.DesiredSize.Height, child.DesiredSize.Width);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Children.Count == 0) return finalSize;

            var child = Children[0];

            double childWidth = IsSideways ? finalSize.Height : finalSize.Width;
            double childHeight = IsSideways ? finalSize.Width : finalSize.Height;
            child.Arrange(new Rect(0, 0, childWidth, childHeight));

            child.RenderTransform = new MatrixTransform { Matrix = BuildMatrix(finalSize.Width, childWidth, childHeight) };

            return finalSize;
        }

        // maps the child, laid out at childWidth x childHeight from the origin, onto the panel:
        // x' = x * M11 + y * M21 + OffsetX
        // y' = x * M12 + y * M22 + OffsetY
        private Matrix BuildMatrix(double panelWidth, double childWidth, double childHeight)
        {
            var (m11, m12, m21, m22, offsetX, offsetY) = NormalizedTurns switch
            {
                1 => (0.0, 1.0, -1.0, 0.0, childHeight, 0.0),
                2 => (-1.0, 0.0, 0.0, -1.0, childWidth, childHeight),
                3 => (0.0, -1.0, 1.0, 0.0, 0.0, childWidth),
                _ => (1.0, 0.0, 0.0, 1.0, 0.0, 0.0)
            };

            if (IsMirrored)
            {
                m11 = -m11;
                m21 = -m21;
                offsetX = panelWidth - offsetX;
            }

            return new Matrix
            {
                M11 = m11,
                M12 = m12,
                M21 = m21,
                M22 = m22,
                OffsetX = offsetX,
                OffsetY = offsetY
            };
        }
    }
}
