namespace FluentSensors.Persistence.Models
{
    // taskbar widget settings kept once per screen edge the taskbar can sit on; a slot on a side taskbar has a
    // different shape than one on a horizontal taskbar, so a width or time range tuned for one rarely suits the other
    // everything else about the taskbar widget and flyout (graph colors, background material, position lock) is
    // shared by all edges and lives directly in AppSettingsData
    public class TaskbarEdgeSettings
    {
        public double GraphTimeSpanSeconds { get; set; } = 35;
        public int GraphWidthDip { get; set; } = 100; // slot length along the taskbar, the slot height on a side taskbar

        // flyout placement over the taskbar widget: "Center", "Left" or "Right" (top and bottom on a side taskbar)
        public string FlyoutAlignment { get; set; } = "Center";

        // only used on the left and right edge
        // graph direction: "RightToLeft" (as on a horizontal taskbar), "TopToBottom" or "BottomToTop", named after the
        // way the values run, so "TopToBottom" brings new values in at the top
        public string SideGraphDirection { get; set; } = "RightToLeft";
        public int SideTitleLines { get; set; } = 1; // 1 or 2 lines for the sensor name

        public TaskbarEdgeSettings Clone() => (TaskbarEdgeSettings)MemberwiseClone();
    }
}
