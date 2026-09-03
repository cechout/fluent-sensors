using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;


namespace FluentSensors.Controls
{
    // arranges its children into a single row by default; only once the available width can no longer
    // fit them all at MinCellWidth does it give up columns one at a time (gaining rows instead)
    //
    // row height is not uniform: each row takes on the natural height of its tallest child instead of a shared
    // forced height, so children with more content (e.g. more stacked graphs) end up in a taller row than
    // children with less, instead of every row being stretched to match
    public class SquareGridPanel : Panel
    {
        // === bindable properties ===

        // below this width per cell, one more column gets dropped
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

        // floor for any single rows height; a row whose tallest child is still shorter than this gets padded up
        // to it instead of collapsing to near-zero
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

        // shared change handler for both properties above; triggers a fresh layout pass whenever either one changes
        private static void OnLayoutAffectingPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SquareGridPanel panel) panel.InvalidateMeasure();
        }


        // === layout overrides ===

        // works out the row/column count for the current width, measures every child at that column width with
        // unconstrained height so it reports its own natural size, then sums each rows tallest child into the
        // total desired height
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

        // places every child into its row/column slot, reusing each childs DesiredSize from the Measure pass
        // above rather than remeasuring
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

        // determines how many columns fit at minCellWidth for the given width, then derives the row count from
        // that; starts from a single row and only drops columns as far as the width forces it
        private static (int rows, int columns) GetGridSize(int count, double availableWidth, double minCellWidth)
        {
            int maxColumnsForWidth = availableWidth > 0
                ? Math.Max(1, (int)Math.Floor(availableWidth / minCellWidth))
                : 1;

            int columns = Math.Min(count, maxColumnsForWidth);

            int rows = (int)Math.Ceiling(count / (double)columns);

            return (rows, columns);
        }

        // tallest childs measured height among the cells in this row, floored at MinCellHeight
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
