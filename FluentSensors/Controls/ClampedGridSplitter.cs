using Microsoft.UI.Xaml.Controls;
using System;

using CommunityToolkit.WinUI.Controls;


namespace FluentSensors.Controls
{
    // the clamped grid splitter:
    // lands exactly on the MinWidth and MaxWidth of a column; explicit column pairs only,
    // BasedOnAlignment passes through
    //
    // --- workaround: GridSplitter stops short of a column limit ---
    // problem: the toolkit drops a whole drag step that would pass a MinWidth or MaxWidth (IsValidColumnWidth) instead
    // of cutting it, so a fast drag stops up to one mouse step early; confirmed in the shipped version:
    // https://github.com/CommunityToolkit/Windows/blob/v8.2.251219/components/Sizers/src/GridSplitter/GridSplitter.Events.cs
    // fix: cut each step to what fits both columns before the toolkit sees it
    public class ClampedGridSplitter : GridSplitter
    {
        // === fields ===

        // a hair inside the limit; the toolkit rejects a rounding error past it
        private const double LimitTolerance = 0.01;

        // the two columns of the drag and their widths at its start
        private ColumnDefinition? _leftColumn;
        private ColumnDefinition? _rightColumn;
        private double _leftStartWidth;
        private double _rightStartWidth;


        // === drag ===

        protected override void OnDragStarting()
        {
            base.OnDragStarting();

            (_leftColumn, _rightColumn) = ResolveColumns();
            _leftStartWidth = _leftColumn?.ActualWidth ?? 0;
            _rightStartWidth = _rightColumn?.ActualWidth ?? 0;
        }

        // the change is the total distance since the drag start; the left column grows by it, the right one shrinks
        protected override bool OnDragHorizontal(double horizontalChange)
        {
            if (_leftColumn != null && _rightColumn != null)
            {
                double lowest = Math.Max(_leftColumn.MinWidth - _leftStartWidth, _rightStartWidth - _rightColumn.MaxWidth);
                double highest = Math.Min(_leftColumn.MaxWidth - _leftStartWidth, _rightStartWidth - _rightColumn.MinWidth);

                // a column already outside its limits at the start (the window shrank) stays with the toolkit
                if (lowest + LimitTolerance <= highest - LimitTolerance)
                {
                    horizontalChange = Math.Clamp(horizontalChange, lowest + LimitTolerance, highest - LimitTolerance);
                }
            }

            return base.OnDragHorizontal(horizontalChange);
        }


        // === private helpers ===

        // the same pair the toolkit resizes for each explicit ResizeBehavior
        private (ColumnDefinition?, ColumnDefinition?) ResolveColumns()
        {
            if (Parent is not Grid grid) return (null, null);

            int column = Grid.GetColumn(this);
            (int left, int right) = ResizeBehavior switch
            {
                GridResizeBehavior.PreviousAndCurrent => (column - 1, column),
                GridResizeBehavior.CurrentAndNext => (column, column + 1),
                GridResizeBehavior.PreviousAndNext => (column - 1, column + 1),
                _ => (-1, -1)
            };

            if (left < 0 || right >= grid.ColumnDefinitions.Count) return (null, null);

            return (grid.ColumnDefinitions[left], grid.ColumnDefinitions[right]);
        }
    }
}
