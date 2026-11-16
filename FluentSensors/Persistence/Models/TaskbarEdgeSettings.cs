namespace FluentSensors.Persistence.Models
{
    // the per-edge taskbar settings:
    // a side slot has another shape than a horizontal one, so its cards rarely suit both; the rest (material, position
    // lock) is shared in AppSettingsData
    // - which card the edge shows, the graph or the compact card with its level bar
    // - the settings of each card, so a switch keeps both (TaskbarCardSettings)
    // - the flyout position over the widget
    public class TaskbarEdgeSettings
    {
        public bool CompactCards { get; set; } = false; // a level bar of the current value in place of the graph

        // null in an older file, SettingsService seeds them from the flat values below
        public TaskbarCardSettings? Graph { get; set; }
        public TaskbarCardSettings? Compact { get; set; }

        // over the widget: "Center", "Left" or "Right" (top and bottom on a side taskbar)
        public string FlyoutAlignment { get; set; } = "Left";

        // --- legacy, read once on load and never written back ---

        // the card values an edge held itself before the cards; SettingsService copies them onto both cards once
        public double? GraphTimeSpanSeconds { get; set; }
        public int? GraphWidthDip { get; set; }
        public string? SideGraphDirection { get; set; }
        public int? SideTitleLines { get; set; }

        public TaskbarEdgeSettings Clone() => new()
        {
            CompactCards = CompactCards,
            Graph = Graph?.Clone(),
            Compact = Compact?.Clone(),
            FlyoutAlignment = FlyoutAlignment
        };
    }
}
