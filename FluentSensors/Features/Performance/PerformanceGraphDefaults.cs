using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance
{
    // the performance graph time spans:
    // the overview blocks take the longer span, the dense grids (cpu all threads, gpu extended) a shorter one each; all
    // are settings a graph follows while on screen
    public enum PerformanceGraphKind
    {
        Standard,
        CpuExtended,
        GpuExtended
    }

    public static class PerformanceGraphDefaults
    {
        public static double StandardTimeSpanSeconds => SettingsService.Instance.PerformanceGraphTimeSpanSeconds;
        public static double CpuExtendedTimeSpanSeconds => SettingsService.Instance.PerformanceCpuExtendedGraphTimeSpanSeconds;
        public static double GpuExtendedTimeSpanSeconds => SettingsService.Instance.PerformanceGpuExtendedGraphTimeSpanSeconds;

        // every SensorPanelControl under root on the setting while root is loaded
        // right away for literal children, on Loaded for the x:Load="False" grids, on every change;
        // an unchanged value is a no-op
        public static void BindTimeSpan(FrameworkElement root, PerformanceGraphKind kind)
        {
            bool isSubscribed = false;

            void Apply() => ApplyTimeSpan(root, ResolveTimeSpanSeconds(kind));

            Apply();

            root.Loaded += (s, e) =>
            {
                Apply();

                if (isSubscribed) return; // a second Loaded must not stack handlers
                isSubscribed = true;
                SettingsService.Instance.PerformanceGraphTimeSpanChanged += Apply;
            };

            root.Unloaded += (s, e) =>
            {
                if (!isSubscribed) return;
                isSubscribed = false;
                SettingsService.Instance.PerformanceGraphTimeSpanChanged -= Apply;
            };
        }

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

        private static double ResolveTimeSpanSeconds(PerformanceGraphKind kind) => kind switch
        {
            PerformanceGraphKind.CpuExtended => CpuExtendedTimeSpanSeconds,
            PerformanceGraphKind.GpuExtended => GpuExtendedTimeSpanSeconds,
            _ => StandardTimeSpanSeconds
        };
    }
}
