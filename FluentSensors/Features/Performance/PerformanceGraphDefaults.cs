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
        public const double GpuExtendedTimeSpanSeconds = 30;

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

        // walks every SensorGraphControl under root and switches its live rendering on or off
        // the Performance page calls this so only the visible detail views graphs keep drawing, while the hidden
        // ones stop doing per-tick work entirely without being destroyed
        public static void SetGraphsRenderingActive(DependencyObject root, bool active)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                if (child is SensorGraphControl graph)
                {
                    graph.SetRenderingActive(active);
                }

                SetGraphsRenderingActive(child, active);
            }
        }
    }
}
