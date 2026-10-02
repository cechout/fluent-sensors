using FluentSensors.Common.Sensors;


namespace FluentSensors.Persistence.Models
{
    // the threshold of one sensor, the same in every view
    public class SensorThreshold
    {
        public bool IsEnabled { get; set; } = false;

        // null until customized; the SensorTypeProfiles default stands in on first use
        public double? Value { get; set; } = null;
        public ThresholdDirection Direction { get; set; } = ThresholdDirection.Above;
        public Windows.UI.Color Color { get; set; } = Windows.UI.Color.FromArgb(255, 231, 72, 86);
    }
}
