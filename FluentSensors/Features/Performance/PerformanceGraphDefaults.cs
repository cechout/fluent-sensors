using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Features.Performance
{
    // hardcoded graph time spans for the Performance page
    //
    // unlike the Widget, this is not user configurable
    public static class PerformanceGraphDefaults
    {
        public const double StandardTimeSpanSeconds = 45;
        public const double CpuThreadTimeSpanSeconds = 30;

        // walks every SensorPanelControl under root and applies timeSpanSeconds to it
        public static void ApplyTimeSpan(DependencyObject root, double timeSpanSeconds)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                if (child is SensorPanelControl panel)
                {
                    panel.GraphTimeSpanOverrideSeconds = timeSpanSeconds;
                }

                ApplyTimeSpan(child, timeSpanSeconds);
            }
        }
    }
}
