using FluentSensors.Common.Sensors;
using FluentSensors.Controls;


namespace FluentSensors.Persistence.Models
{
    // Y-axis configuration (auto-scaling flag and optional manual max) for a specific presentation scope
    public class SensorYAxisState
    {
        public bool IsAutoScaled { get; set; } = true;

        // null means "never customized by the user", resolved against SensorTypeProfiles default
        public double? ManualYMax { get; set; } = null;
    }

    // full configurable state for one sensor
    // keyed by its stable LibreHardwareMonitor SensorId
    // bundles everything the user can set per sensor: visibility, threshold (global), and per-scope Y-axis scaling
    public class SensorState
    {
        public bool IsHidden { get; set; }
        public SensorThreshold Threshold { get; set; } = new SensorThreshold();

        // independent Y-axis scaling per profile
        public SensorYAxisState PerformanceYAxis { get; set; } = new SensorYAxisState();
        public SensorYAxisState WidgetYAxis { get; set; } = new SensorYAxisState();
        public SensorYAxisState TaskbarYAxis { get; set; } = new SensorYAxisState();

        // legacy fallback properties for backward compatibility with older saved settings
        public bool IsAutoScaled
        {
            get => WidgetYAxis.IsAutoScaled;
            set => WidgetYAxis.IsAutoScaled = value;
        }

        public double? ManualYMax
        {
            get => WidgetYAxis.ManualYMax;
            set => WidgetYAxis.ManualYMax = value;
        }

        public SensorYAxisState GetYAxis(SensorGraphScope scope) => scope switch
        {
            SensorGraphScope.Performance => PerformanceYAxis,
            SensorGraphScope.Widget => WidgetYAxis,
            SensorGraphScope.Taskbar => TaskbarYAxis,
            _ => WidgetYAxis
        };
    }
}