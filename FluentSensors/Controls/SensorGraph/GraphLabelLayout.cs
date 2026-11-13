namespace FluentSensors.Controls.SensorGraph
{
    // name and value over a taskbar graph:
    // size and weight - FontSize in DIP for both, IsValueSemiBold for the value
    // arrangement - IsStacked puts the value under the name, else name left and value right in one row
    // position - IsAtBottom moves the text to the foot of the graph, IsRightAligned moves stacked text to the right
    // (a side slot reads only size, weight and IsRightAligned)
    public sealed record GraphLabelLayout(double FontSize, bool IsValueSemiBold, bool IsStacked, bool IsAtBottom, bool IsRightAligned)
    {
        public static readonly GraphLabelLayout Default = new(10, false, false, false, false);
    }
}
