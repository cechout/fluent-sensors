namespace FluentSensors.Controls.SensorGraph
{
    // name and value over a taskbar graph:
    // size and weight - FontSize in DIP for both, IsValueSemiBold for the value
    // arrangement - IsStacked puts the value under the name, else name left and value right in one row
    // position - IsAtBottom moves the text to the foot of the graph, IsRightAligned moves stacked text to the right
    // side slot - reads size, weight, IsRightAligned and SidePosition
    public sealed record GraphLabelLayout(
        double FontSize,
        bool IsValueSemiBold,
        bool IsStacked,
        bool IsAtBottom,
        bool IsRightAligned,
        SideLabelPosition SidePosition)
    {
        public static readonly GraphLabelLayout Default = new(10, false, false, false, false, SideLabelPosition.Spread);
    }

    // name and value on a side taskbar: name at the top and value at the bottom, or both stacked at one end
    public enum SideLabelPosition
    {
        Spread,
        Top,
        Bottom
    }
}
