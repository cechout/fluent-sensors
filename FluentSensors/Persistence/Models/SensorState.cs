using FluentSensors.Common.Sensors;
using FluentSensors.Controls;


namespace FluentSensors.Persistence.Models
{
    // the y-axis of one scope: auto scaling and an optional manual max
    public class SensorYAxisState
    {
        public bool IsAutoScaled { get; set; } = true;

        // null until customized; the SensorTypeProfiles default stands in
        public double? ManualYMax { get; set; } = null;
    }

    // everything configurable for one sensor, keyed by its LHM SensorId: visibility, the threshold
    // (global) and the y-axis per scope
    public class SensorState
    {
        public bool IsHidden { get; set; }
        public SensorThreshold Threshold { get; set; } = new SensorThreshold();

        public SensorYAxisState PerformanceYAxis { get; set; } = new SensorYAxisState();
        public SensorYAxisState WidgetYAxis { get; set; } = new SensorYAxisState();
        public SensorYAxisState TaskbarYAxis { get; set; } = new SensorYAxisState();

        // legacy, for older saved settings
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
