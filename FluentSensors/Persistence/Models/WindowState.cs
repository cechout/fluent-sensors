using System.Collections.Generic;


namespace FluentSensors.Persistence.Models
{
    // persisted position and size for one window
    // the dictionary key in window-state.json identifies which window this belongs to (e.g. "Main", "Widget")
    public class WindowState
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsMaximized { get; set; }

        // WidgetWindow only: whether it was open when the app last closed, so it can be automatically restored on
        // next launch; which sensors to restore it with comes from SensorSelectionService, not from here
        public bool WasOpen { get; set; }

        // legacy: kept only so SensorSelectionService.MigrateFromLegacyWidgetPins can still read a pre-update
        // window-state.json on someone elses first launch after updating
        // current code never writes this anymore, SensorSelectionService owns the pinned selection now
        public List<string> PinnedSensorIds { get; set; } = new();
    }
}
