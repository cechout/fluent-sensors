using Microsoft.UI.Xaml.Controls;
using System;

using CommunityToolkit.WinUI.Controls;


namespace FluentSensors.Controls
{
    // a GridSplitter that lands exactly on a columns MinWidth and MaxWidth
    // only the explicit column pairs are handled; BasedOnAlignment passes every step through unchanged
    //
    // --- workaround: GridSplitter stops short of a column limit ---
    // problem: the toolkit splitter checks every drag step with IsValidColumnWidth and drops the whole step when it
    // would carry a column past its MinWidth or MaxWidth, instead of cutting it down to the limit; a fast drag
    // therefore stopped up to one mouse step early, and a second drag could still pull the panel a few pixels further
    // confirmed in the source of the version this app ships:
    // https://github.com/CommunityToolkit/Windows/blob/v8.2.251219/components/Sizers/src/GridSplitter/GridSplitter.Events.cs
    // fix: cut each step down to what still fits both columns before the toolkit sees it
    public class ClampedGridSplitter : GridSplitter
    {
        // === fields ===

        // keeps a cut step a hair inside the limit, since the toolkit rejects a width a rounding error past it
        private const double LimitTolerance = 0.01;

        // the two columns the drag trades width between, left and right, and their widths when it started
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

        // the change is the total distance from where the drag started; the left column grows by it, the right one
        // shrinks by it
        protected override bool OnDragHorizontal(double horizontalChange)
        {
            if (_leftColumn != null && _rightColumn != null)
            {
                double lowest = Math.Max(_leftColumn.MinWidth - _leftStartWidth, _rightStartWidth - _rightColumn.MaxWidth);
                double highest = Math.Min(_leftColumn.MaxWidth - _leftStartWidth, _rightStartWidth - _rightColumn.MinWidth);

                // a column that was already outside its limits when the drag began, e.g. after the window shrank, is
                // left to the toolkit as it is
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
