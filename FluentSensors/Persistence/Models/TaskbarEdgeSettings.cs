namespace FluentSensors.Persistence.Models
{
    // the per-edge taskbar settings:
    // a side slot has another shape than a horizontal one, so width and time range rarely suit both; the rest (graph
    // colors, material, position lock) is shared in AppSettingsData
    public class TaskbarEdgeSettings
    {
        public double GraphTimeSpanSeconds { get; set; } = 35;
        public int GraphWidthDip { get; set; } = 100; // along the taskbar, the height on a side one

        // over the widget: "Center", "Left" or "Right" (top and bottom on a side taskbar)
        public string FlyoutAlignment { get; set; } = "Center";

        // left and right edge only: "RightToLeft" (as horizontal), "TopToBottom" or "BottomToTop", the way values run
        // ("TopToBottom" enters at the top)
        public string SideGraphDirection { get; set; } = "RightToLeft";
        public int SideTitleLines { get; set; } = 1; // 1 or 2

        public TaskbarEdgeSettings Clone() => (TaskbarEdgeSettings)MemberwiseClone();
    }
}
