using FluentHwInfo.Controls;


namespace FluentHwInfo.Persistence.Models
{
    // full configurable state for one sensor
    // keyed by its stable LibreHardwareMonitor SensorId
    // bundles everything the user can set per sensor: visibility, threshold, and widget Y-axis scaling
    public class SensorState
    {
        public bool IsHidden { get; set; }
        public SensorThreshold Threshold { get; set; } = new SensorThreshold();
        public bool IsAutoScaled { get; set; } = true;

        // null means "never customized by the user"
        // gets resolved against a per-sensor-type default (see SensorTypeProfiles) the first time its actually needed
        public double? ManualYMax { get; set; } = null;
        public bool IsSelected { get; set; }
    }
}