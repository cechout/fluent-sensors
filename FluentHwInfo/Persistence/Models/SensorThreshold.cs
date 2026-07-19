using FluentHwInfo.Controls;


namespace FluentHwInfo.Persistence.Models
{
    // snapshot of one sensors threshold configuration
    // shared between MainWindow and WidgetWindow
    public class SensorThreshold
    {
        public bool IsEnabled { get; set; } = false;

        // null means "never customized by the user"
        // gets resolved against a per-sensor-type default (see SensorTypeProfiles) the first time its actually needed
        public double? Value { get; set; } = null;
        public ThresholdDirection Direction { get; set; } = ThresholdDirection.Above;
        public Windows.UI.Color Color { get; set; } = Windows.UI.Color.FromArgb(255, 220, 50, 50);
    }
}