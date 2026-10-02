using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;


namespace FluentSensors.Controls
{
    // the square grid panel:
    // one row, dropping a column at a time once the width cannot fit all at MinCellWidth
    // each row takes the natural height of its tallest child, so more stacked graphs make a taller row
    public class SquareGridPanel : Panel
    {
        // === bindable properties ===

        // below this per cell a column drops
        public double MinCellWidth
        {
            get => (double)GetValue(MinCellWidthProperty);
            set => SetValue(MinCellWidthProperty, value);
        }
        public static readonly DependencyProperty MinCellWidthProperty =
            DependencyProperty.Register(
                nameof(MinCellWidth),
                typeof(double),
                typeof(SquareGridPanel),
                new PropertyMetadata(130.0, OnLayoutAffectingPropertyChanged));

        // the row height floor
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

        // between cells in both directions, instead of item margins
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

        // a new layout pass on any of them
        private static void OnLayoutAffectingPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SquareGridPanel panel) panel.InvalidateMeasure();
        }


        // === layout overrides ===

        // every child at the column width and unconstrained height; the desired height sums the tallest child per row
        protected override Size MeasureOverride(Size availableSize)
        {
            int count = Children.Count;
            if (count == 0) return new Size(0, 0);

            double measureWidth = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;

            var (rows, columns) = GetGridSize(count, measureWidth, MinCellWidth);

            double cellWidth = Math.Max(0, (measureWidth - Spacing * (columns - 1)) / columns);

            var cellSize = new Size(cellWidth, double.PositiveInfinity);
            foreach (var child in Children)
            {
                child.Measure(cellSize);
            }

            double totalHeight = 0;
            for (int row = 0; row < rows; row++)
            {
                totalHeight += GetRowHeight(row, columns, count);
            }
            totalHeight += Spacing * Math.Max(0, rows - 1);

            return new Size(measureWidth, totalHeight);
        }

        // every child into its slot, from its DesiredSize without remeasuring
        protected override Size ArrangeOverride(Size finalSize)
        {
            int count = Children.Count;
            if (count == 0) return finalSize;

            var (rows, columns) = GetGridSize(count, finalSize.Width, MinCellWidth);

            double cellWidth = Math.Max(0, (finalSize.Width - Spacing * (columns - 1)) / columns);

            double y = 0;
            for (int row = 0; row < rows; row++)
            {
                double rowHeight = GetRowHeight(row, columns, count);

                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column;
                    if (index >= count) break;

                    double x = column * (cellWidth + Spacing);
                    Children[index].Arrange(new Rect(x, y, cellWidth, rowHeight));
                }

                y += rowHeight + Spacing;
            }

            return finalSize;
        }


        // === private helpers ===

        // the columns that fit at minCellWidth, from a single row down, and the rows from that
        private static (int rows, int columns) GetGridSize(int count, double availableWidth, double minCellWidth)
        {
            int maxColumnsForWidth = availableWidth > 0
                ? Math.Max(1, (int)Math.Floor(availableWidth / minCellWidth))
                : 1;

            int columns = Math.Min(count, maxColumnsForWidth);

            int rows = (int)Math.Ceiling(count / (double)columns);

            return (rows, columns);
        }

        // the tallest child in the row, at least MinCellHeight
        private double GetRowHeight(int row, int columns, int count)
        {
            double tallest = 0;
            for (int column = 0; column < columns; column++)
            {
                int index = row * columns + column;
                if (index >= count) break;

                tallest = Math.Max(tallest, Children[index].DesiredSize.Height);
            }

            return Math.Max(tallest, MinCellHeight);
        }
    }
}
