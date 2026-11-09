using System.Collections.Generic;


namespace FluentSensors.Persistence.Models
{
    // the sensor ids of the selection profiles:
    // - widget windows
    // - csv
    // - taskbar
    // - widgets board
    // membership only, a consumer orders them by discovery
    public class SensorSelectionState
    {
        public List<string> WidgetWindow { get; set; } = new();
        // widget windows 2 and up, in order; window 1 stays in WidgetWindow, where an older version finds it
        public List<List<string>> ExtraWidgetWindows { get; set; } = new();
        public List<string> Csv { get; set; } = new();
        public List<string> Taskbar { get; set; } = new();
        public List<string> TaskbarSensorOrder { get; set; } = new();
        public bool TaskbarSensorsDefaultToTray { get; set; }
        public Dictionary<string, bool> TaskbarSensorTrayOverrides { get; set; } = new();
        public HashSet<string> CompactTaskbarSensors { get; set; } = new();
        public List<string> WidgetBoard { get; set; } = new();

        // guards the one-time migration from WindowState "Widget" PinnedSensorIds; stays true, even
        // for an emptied WidgetWindow
        public bool HasMigratedLegacyWidgetSelection { get; set; }
    }
}
