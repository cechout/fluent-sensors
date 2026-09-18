using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance
{
    // which graphs on the Performance page plot which time span
    //
    // the two kinds exist because the page has two densities: the overview blocks get the longer window, the
    // grids that show many small graphs at once (cpu all-threads, gpu extended) usually want a shorter one
    // both are user configurable, so a graph has to follow the setting for as long as it is on screen, not just
    // pick it up once when its view is built
    public enum PerformanceGraphKind
    {
        Standard,
        Extended
    }

    public static class PerformanceGraphDefaults
    {
        public static double StandardTimeSpanSeconds => SettingsService.Instance.PerformanceGraphTimeSpanSeconds;
        public static double ExtendedTimeSpanSeconds => SettingsService.Instance.PerformanceExtendedGraphTimeSpanSeconds;

        // keeps every SensorPanelControl under root on the current setting for as long as root is loaded
        //
        // applies once right away for roots whose children are literal xaml and already exist, once more on Loaded
        // for the x:Load="False" grids that are only realized when the user actually opens them, and again on every
        // settings change; re-applying an unchanged value is a no-op all the way down
        public static void BindTimeSpan(FrameworkElement root, PerformanceGraphKind kind)
        {
            bool isSubscribed = false;

            void Apply() => ApplyTimeSpan(root, ResolveTimeSpanSeconds(kind));

            Apply();

            root.Loaded += (s, e) =>
            {
                Apply();

                if (isSubscribed) return; // a second Loaded without an Unloaded in between must not stack handlers
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

        private static double ResolveTimeSpanSeconds(PerformanceGraphKind kind) => kind switch
        {
            PerformanceGraphKind.Extended => ExtendedTimeSpanSeconds,
            _ => StandardTimeSpanSeconds
        };
    }
}
