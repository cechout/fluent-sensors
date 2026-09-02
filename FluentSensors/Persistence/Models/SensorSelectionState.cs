using System.Collections.Generic;


namespace FluentSensors.Persistence.Models
{
    // persisted sensor ids for the three selection profiles (widget window, csv, taskbar)
    // this is membership only, in the order the checkboxes happened to be toggled; the order a consumer displays
    // is resolved from hardware discovery order at load time, not from this list
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
