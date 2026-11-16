namespace FluentSensors.Persistence.Models
{
    // the per-edge taskbar settings:
    // a side slot has another shape than a horizontal one, so width and time range rarely suit both; the rest (graph
    // colors, material, position lock) is shared in AppSettingsData
    public class TaskbarEdgeSettings
    {
        public double GraphTimeSpanSeconds { get; set; } = 35;
        public int GraphWidthDip { get; set; } = 100; // along the taskbar, the height on a side one

        // left and right edge only:
        // - "RightToLeft" (as horizontal)
        // - "TopToBottom" (enters at the top)
        // - "BottomToTop", the way values run
        public string SideGraphDirection { get; set; } = "RightToLeft";
        public int SideTitleLines { get; set; } = 1; // 1 or 2

        // name and value over the graph:
        // - arrangement and position (top and bottom edge only): "SideBySide" (name left, value right) or "Stacked",
        // - at the "Top" or "Bottom" of the graph
        // - alignment: "Left" or "Right": stacked text on a top or bottom edge, always on a side edge
        // - side position: where name and value sit on a side edge
        public string LabelArrangement { get; set; } = "SideBySide";
        public string LabelPosition { get; set; } = "Top";
        public string LabelAlignment { get; set; } = "Left";
        public string SideLabelPosition { get; set; } = "Spread"; // side edge only: "Spread" (name top, value bottom), "Top" or "Bottom"

        // over the widget: "Center", "Left" or "Right" (top and bottom on a side taskbar)
        public string FlyoutAlignment { get; set; } = "Left";

        public TaskbarEdgeSettings Clone() => (TaskbarEdgeSettings)MemberwiseClone();
    }
}
