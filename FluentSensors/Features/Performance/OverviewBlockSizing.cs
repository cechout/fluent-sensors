using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;


namespace FluentSensors.Features.Performance
{
    // the overview block sizing:
    // the overview block of all five detail views fills the height the viewport has left and scrolls
    // once its content no longer fits
    // the rest is read off the live tree, so XAML changes count on their own: the scroll content inset, its
    // other shown children with margins and spacing, and margin and inset of each container from the block
    // up, its own margin included
    // relies on the shape: the block sits inside one child of the StackPanel scroll content, beside nothing taller
    public static class OverviewBlockSizing
    {
        // === fields ===

        // layout rounding snaps to device pixels, and at 125% or 175% a DIP value lands on half a pixel
        // (spacing 14 is 24.5px at 175%)
        // that adds up to a pixel or two the DIP arithmetic cannot see and would scroll every view; held
        // back, it leaves a sliver at most
        private const double LayoutRoundingAllowanceDip = 2;


        // === public api ===

        // the viewport minus everything around the block, never below its natural minimum
        public static double Height(ScrollViewer scrollViewer, StackPanel content, FrameworkElement block, double naturalMinHeight)
        {
            Thickness contentInset = Inset(content);
            double space = scrollViewer.Padding.Top + scrollViewer.Padding.Bottom + contentInset.Top + contentInset.Bottom;

            FrameworkElement holder = block;
            foreach (var element in ChainUpTo(block, content))
            {
                space += element.Margin.Top + element.Margin.Bottom;

                // the block inset is inside its height, only the containers above add theirs
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

        // the content width of the block, for measuring before it has its final size
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

        // the block and every container above it, without the scroll content
        private static IEnumerable<FrameworkElement> ChainUpTo(FrameworkElement block, Panel content)
        {
            for (var current = block; current != null && current != content; current = current.Parent as FrameworkElement)
            {
                yield return current;
            }
        }

        // padding plus border
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
