using System.Collections.Generic;

using FluentSensors.Common.UI;


namespace FluentSensors.Persistence.Models
{
    // position and size of one window, keyed in window-state.json ("Main", "Widget", "Widget2", ...)
    public class WindowState
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsMaximized { get; set; }

        // WidgetWindow only, reopened on the next launch; its sensors come from SensorSelectionService
        public bool WasOpen { get; set; }

        // WidgetWindow only, per window; null for a window that never picked a range, it starts on the
        // GraphTimeSpanSeconds setting
        public WindowZOrder ZOrder { get; set; } = WindowZOrder.AlwaysOnTop;
        public double? GraphTimeSpanSeconds { get; set; }

        // legacy, read only by SensorSelectionService.MigrateFromLegacyWidgetPins on the first launch
        // after an update; never written
        public List<string> PinnedSensorIds { get; set; } = new();
    }
}
