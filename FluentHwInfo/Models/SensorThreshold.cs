using FluentHwInfo.Controls;

namespace FluentHwInfo.Models
{
    // snapshot of one sensors threshold configuration
    // shared between MainWindow and WidgetWindow
    public class SensorThreshold
    {
        public bool IsEnabled { get; set; }
        public double Value { get; set; }
        public ThresholdDirection Direction { get; set; }
        public Windows.UI.Color Color { get; set; }
    }
}