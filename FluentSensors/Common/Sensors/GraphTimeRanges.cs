using System.Collections.Generic;
using System.Linq;


namespace FluentSensors.Common.Sensors
{
    // one time range option; DisplayName is the dropdown text
    public class GraphTimeRange
    {
        public double Seconds { get; }
        public string DisplayName => $"{Seconds:0}s";

        public GraphTimeRange(double seconds)
        {
            Seconds = seconds;
        }
    }

    // the time range options:
    // one list per time range setting, read by the settings page and every TimeRangePickerControl, so both always
    // offer the same
    public static class GraphTimeRanges
    {
        public static IReadOnlyList<GraphTimeRange> Performance { get; } = Create(30, 45, 60, 75, 90, 120, 180, 240);
        public static IReadOnlyList<GraphTimeRange> PerformanceExtended { get; } = Create(15, 20, 25, 30, 35, 40, 45, 60, 90);
        public static IReadOnlyList<GraphTimeRange> Widget { get; } = Create(15, 20, 30, 45, 60, 75, 90, 120, 180, 240);
        public static IReadOnlyList<GraphTimeRange> Taskbar { get; } = Create(15, 20, 25, 30, 35, 40, 45, 60, 90, 120);

        // the option of seconds in options, null when the list does not have it
        public static GraphTimeRange? Find(IReadOnlyList<GraphTimeRange>? options, double seconds)
        {
            return options?.FirstOrDefault(o => o.Seconds == seconds);
        }

        private static IReadOnlyList<GraphTimeRange> Create(params double[] seconds)
        {
            return seconds.Select(s => new GraphTimeRange(s)).ToList();
        }
    }
}
