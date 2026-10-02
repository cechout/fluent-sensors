using System.Collections.Generic;


namespace FluentSensors.Persistence.Models
{
    // the sensor ids of the three selection profiles (widget window, csv, taskbar); membership only, a
    // consumer orders them by discovery
    public class SensorSelectionState
    {
        public List<string> WidgetWindow { get; set; } = new();
        public List<string> Csv { get; set; } = new();
        public List<string> Taskbar { get; set; } = new();

        // guards the one-time migration from WindowState "Widget" PinnedSensorIds; stays true, even
        // for an emptied WidgetWindow
        public bool HasMigratedLegacyWidgetSelection { get; set; }
    }
}
