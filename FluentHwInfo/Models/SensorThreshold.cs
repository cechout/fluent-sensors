using FluentHwInfo.Controls;

namespace FluentHwInfo.Models
{
    // snapshot of one sensors threshold configuration
    // shared between MainWindow and WidgetWindow
    public class SensorThreshold
    {
        public bool IsEnabled { get; set; } = false;
        public double Value { get; set; } = 50;
        public ThresholdDirection Direction { get; set; } = ThresholdDirection.Above;
        public Windows.UI.Color Color { get; set; } = Windows.UI.Color.FromArgb(255, 220, 50, 50);
    }
}