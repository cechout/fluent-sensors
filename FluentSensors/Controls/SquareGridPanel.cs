using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;


namespace FluentSensors.Controls
{
    // arranges its children into a grid that grows roughly square as the child count grows (1-3 children get
    // one row, 4-8 get two rows, 9-15 get three, and so on - a new row starts exactly at each perfect square),
    // but caps the column count once the available width gets too narrow for that many columns to stay usable.
    // rows are never capped, they simply grow to absorb whatever the capped columns can't hold.
    // also enforces a minimum cell height (MinCellHeight): once rows would otherwise get shorter than that, the
    // panel reports a taller DesiredSize instead of squeezing cells further - wrapped in a ScrollViewer, this
    // makes the page scroll instead of the content becoming unusably small.
    public class SquareGridPanel : Panel
    {
        // below this width per cell, one more column gets dropped
        private const double MinCellWidth = 110;

        public double MinCellHeight
        {
            get => (double)GetValue(MinCellHeightProperty);
            set => SetValue(MinCellHeightProperty, value);
        }

        public static readonly DependencyProperty MinCellHeightProperty =
            DependencyProperty.Register(
                nameof(MinCellHeight),
                typeof(double),
                typeof(SquareGridPanel),
                new PropertyMetadata(0.0, OnLayoutAffectingPropertyChanged));

        // gap between cells, applied both horizontally and vertically; set centrally here instead of via Margin
        // on individual items
        public double Spacing
        {
            get => (double)GetValue(SpacingProperty);
            set => SetValue(SpacingProperty, value);
        }

        public static readonly DependencyProperty SpacingProperty =
            DependencyProperty.Register(
                nameof(Spacing),
                typeof(double),
                typeof(SquareGridPanel),
                new PropertyMetadata(0.0, OnLayoutAffectingPropertyChanged));

        private static void OnLayoutAffectingPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SquareGridPanel panel) panel.InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            int count = Children.Count;
            if (count == 0) return new Size(0, 0);

            double measureWidth = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
            double measureHeight = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;

            var (rows, columns) = GetGridSize(count, measureWidth);

            double cellWidth = Math.Max(0, (measureWidth - Spacing * (columns - 1)) / columns);
            double rawCellHeight = Math.Max(0, (measureHeight - Spacing * (rows - 1)) / rows);
            double cellHeight = Math.Max(rawCellHeight, MinCellHeight);

            double desiredHeight = Math.Max(measureHeight, cellHeight * rows + Spacing * (rows - 1));

            var cellSize = new Size(cellWidth, cellHeight);
            foreach (var child in Children)
            {
                child.Measure(cellSize);
            }

            return new Size(measureWidth, desiredHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            int count = Children.Count;
            if (count == 0) return finalSize;

            var (rows, columns) = GetGridSize(count, finalSize.Width);

            double cellWidth = Math.Max(0, (finalSize.Width - Spacing * (columns - 1)) / columns);
            double rawCellHeight = Math.Max(0, (finalSize.Height - Spacing * (rows - 1)) / rows);
            double cellHeight = Math.Max(rawCellHeight, MinCellHeight);

            for (int i = 0; i < count; i++)
            {
                int row = i / columns;
                int column = i % columns;

                double x = column * (cellWidth + Spacing);
                double y = row * (cellHeight + Spacing);

                Children[i].Arrange(new Rect(x, y, cellWidth, cellHeight));
            }

            return finalSize;
        }

        private static (int rows, int columns) GetGridSize(int count, double availableWidth)
        {
            int idealRows = (int)Math.Floor(Math.Sqrt(count));
            int idealColumns = (int)Math.Ceiling(count / (double)idealRows);

            int maxColumnsForWidth = availableWidth > 0
                ? Math.Max(1, (int)Math.Floor(availableWidth / MinCellWidth))
                : 1;

            int columns = Math.Min(idealColumns, maxColumnsForWidth);
            int rows = (int)Math.Ceiling(count / (double)columns);

            return (rows, columns);
        }
    }
}