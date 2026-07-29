using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;


namespace FluentSensors.Controls
{
    // arranges its children into a single row by default; only once the available width can no longer
    // fit them all at MinCellWidth does it give up columns one at a time (gaining rows instead); it never
    // deliberately targets a square shape, thats just what falls out naturally at some widths for
    // evenly-divisible counts like 4 or 6
    //
    // also enforces a minimum cell height (MinCellHeight): once rows would otherwise get shorter than that, the
    // panel reports a taller DesiredSize instead of squeezing cells further; wrapped in a ScrollViewer, this
    // makes the page scroll instead of the content becoming unusably small
    public class SquareGridPanel : Panel
    {
        // === fields ===

        // below this width per cell, one more column gets dropped
        private const double MinCellWidth = 130;


        // === bindable properties ===

        // minimum height per cell; if rows would otherwise shrink below this, the panel reports a taller
        // desired height instead of squeezing further, letting a wrapping ScrollViewer take over
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

        // works out the row/column count for the current width, then measures every child against that cell
        // size and reports the resulting desired height back to the parent
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

        // places every child into its row/column slot, using the same row/column count MeasureOverride already
        // determined for this width
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


        // === private helpers ===

        // determines how many columns fit at MinCellWidth for the given width, then derives the row count from
        // that; starts from a single row and only drops columns as far as the width forces it
        private static (int rows, int columns) GetGridSize(int count, double availableWidth)
        {
            int maxColumnsForWidth = availableWidth > 0
                ? Math.Max(1, (int)Math.Floor(availableWidth / MinCellWidth))
                : 1;

            int columns = Math.Min(count, maxColumnsForWidth);

            int rows = (int)Math.Ceiling(count / (double)columns);

            return (rows, columns);
        }
    }
}