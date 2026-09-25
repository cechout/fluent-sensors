using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;


namespace FluentSensors.Features.Performance
{
    // sizes the overview block of a hardware detail view, the same way for all five of them
    //
    // the block fills whatever height the viewport has left and grows past it into a scroll once its own content no
    // longer fits; what is left is read off the live tree instead of numbers kept in code, so a padding, spacing,
    // border or margin changed in the XAML is picked up on its own:
    // the scroll contents inset, every other shown child of it with its margin and the spacing between them, and the
    // margin and inset of each container from the block up to the scroll content, the blocks own margin included
    // the one thing it relies on is the shape: the block sits somewhere inside one child of the StackPanel that is
    // the scroll content, and shares its row with nothing taller than itself
    public static class OverviewBlockSizing
    {
        // === fields ===

        // layout rounding snaps every offset and size to whole device pixels, and at a scale like 125% or 175% a DIP
        // value such as the StackPanel spacing of 14 lands on half a pixel (24.5px at 175%); those roundings add up
        // to a pixel or two the DIP arithmetic below cannot see, which was just enough to make every view scroll by
        // that much
        // held back from the height instead, which leaves at most a sliver of empty space under the block
        private const double LayoutRoundingAllowanceDip = 2;


        // === public api ===

        // the viewport minus everything around the block, but never below the blocks own natural minimum
        public static double Height(ScrollViewer scrollViewer, StackPanel content, FrameworkElement block, double naturalMinHeight)
        {
            Thickness contentInset = Inset(content);
            double space = scrollViewer.Padding.Top + scrollViewer.Padding.Bottom + contentInset.Top + contentInset.Bottom;

            FrameworkElement holder = block;
            foreach (var element in ChainUpTo(block, content))
            {
                space += element.Margin.Top + element.Margin.Bottom;

                // the blocks own inset is inside the height it is given, only the containers above it add theirs
                if (element != block)
                {
                    Thickness inset = Inset(element);
                    space += inset.Top + inset.Bottom;
                }

                holder = element;
            }

            int shownChildren = 0;
            foreach (var child in content.Children)
            {
                if (child.Visibility != Visibility.Visible) continue;
                shownChildren++;

                if (child != holder && child is FrameworkElement sibling)
                {
                    space += sibling.ActualHeight + sibling.Margin.Top + sibling.Margin.Bottom;
                }
            }
            space += content.Spacing * Math.Max(0, shownChildren - 1);

            return Math.Max(scrollViewer.ActualHeight - space - LayoutRoundingAllowanceDip, naturalMinHeight);
        }

        // the width the blocks content is laid out at, for measuring parts of it before the block has its final size
        public static double ContentWidth(ScrollViewer scrollViewer, StackPanel content, FrameworkElement block)
        {
            Thickness contentInset = Inset(content);
            double space = scrollViewer.Padding.Left + scrollViewer.Padding.Right + contentInset.Left + contentInset.Right;

            foreach (var element in ChainUpTo(block, content))
            {
                space += element.Margin.Left + element.Margin.Right;

                if (element != block)
                {
                    Thickness inset = Inset(element);
                    space += inset.Left + inset.Right;
                }
            }

            return Math.Max(0, scrollViewer.ActualWidth - space);
        }


        // === private helpers ===

        // the block and every container above it, up to but not including the scroll content itself
        private static IEnumerable<FrameworkElement> ChainUpTo(FrameworkElement block, Panel content)
        {
            for (var current = block; current != null && current != content; current = current.Parent as FrameworkElement)
            {
                yield return current;
            }
        }

        // padding plus border, the space a container keeps between its own edge and its children
        private static Thickness Inset(FrameworkElement element) => element switch
        {
            Grid grid => Add(grid.Padding, grid.BorderThickness),
            StackPanel stackPanel => Add(stackPanel.Padding, stackPanel.BorderThickness),
            Border border => Add(border.Padding, border.BorderThickness),
            _ => new Thickness(0)
        };

        private static Thickness Add(Thickness a, Thickness b) =>
            new Thickness(a.Left + b.Left, a.Top + b.Top, a.Right + b.Right, a.Bottom + b.Bottom);
    }
}
