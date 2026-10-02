using System.Collections.Generic;


namespace FluentSensors.Persistence.Models
{
    // position and size of one window, keyed in window-state.json ("Main", "Widget")
    public class WindowState
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsMaximized { get; set; }

        // WidgetWindow only, reopened on the next launch; its sensors come from SensorSelectionService
        public bool WasOpen { get; set; }

        // legacy, read only by SensorSelectionService.MigrateFromLegacyWidgetPins on the first launch
        // after an update; never written
        public List<string> PinnedSensorIds { get; set; } = new();
    }
}
