using System.Collections.Generic;


namespace FluentSensors.Persistence.Models
{
    // persisted ordered sensor ids for the three selection profiles (widget window, csv, taskbar)
    // order matters here, it is the exact row order each profiles consumer displays
    public class SensorSelectionState
    {
        public List<string> WidgetWindow { get; set; } = new();
        public List<string> Csv { get; set; } = new();
        public List<string> Taskbar { get; set; } = new();

        // guards the one-time migration from the pre-profile widget pin list (WindowState "Widget" PinnedSensorIds)
        // stays true forever after the first successful run, even if the user later empties WidgetWindow completely
        public bool HasMigratedLegacyWidgetSelection { get; set; }
    }
}
