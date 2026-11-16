using FluentSensors.Common.Sensors;

namespace FluentSensors.Persistence.Models
{
    // the settings of one taskbar card on one edge, the graph or the compact card (TaskbarEdgeSettings):
    // everything the taskbar graph settings show, so a switch of the card switches all of them
    public class TaskbarCardSettings
    {
        // graph look; the color also reaches the flyout graphs
        public GraphColorSource ColorSource { get; set; } = AppSettingsData.DefaultTaskbarGraphColorSource;
        public Windows.UI.Color CustomColor { get; set; } = Microsoft.UI.Colors.LightBlue;
        public bool UseTransparentBackground { get; set; } = true;
        public bool GraphBlur { get; set; } = false; // a slight blur on line and fill, the graph only
        public double GraphTimeSpanSeconds { get; set; } = 35;
        public int GraphWidthDip { get; set; } = 100; // along the taskbar, the height on a side one

        // left and right edge only:
        // - "RightToLeft" (as horizontal)
        // - "TopToBottom" (enters at the top)
        // - "BottomToTop", the way values run
        public string SideGraphDirection { get; set; } = "RightToLeft";
        public int SideTitleLines { get; set; } = 1; // 1 or 2

        // name and value text:
        // - size in DIP (9 to 12) and a SemiBold value
        // - arrangement and position (top and bottom edge only): "SideBySide" (name left, value right) or "Stacked",
        // - at the "Top", in the "Center" or at the "Bottom" of the graph
        // - alignment: "Left" or "Right": stacked text on a top or bottom edge, always on a side edge
        // - side position: where name and value sit on a side edge
        public double LabelFontSize { get; set; } = 10;
        public bool ValueSemiBold { get; set; } = false;
        public string LabelArrangement { get; set; } = "SideBySide";
        public string LabelPosition { get; set; } = "Top";
        public string LabelAlignment { get; set; } = "Left";
        public string SideLabelPosition { get; set; } = "Spread"; // side edge only: "Spread" (name top, value bottom), "Top", "Center" or "Bottom"

        public int? DecimalPlaces { get; set; } // null for automatic

        public TaskbarCardSettings Clone() => (TaskbarCardSettings)MemberwiseClone();
    }
}
