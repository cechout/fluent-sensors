using System;


namespace FluentSensors.Controls.SensorGraph
{
    // one switch candidate:
    // an option of the SensorPanelControl switch combobox; Resolve builds the graph on the first pick and caches it
    public class SensorSwitchCandidate
    {
        public string SensorId { get; }
        public string DisplayName { get; }

        // the default without a saved choice; set at registration, not by discovery order
        public bool IsDefault { get; }

        // an own y-max, null leaves the panel its own; a Func, so a later source (the Total Space of a
        // drive) is read live on activation
        private readonly Func<double?> _yMaxOverride;
        public double? YMaxOverride => _yMaxOverride?.Invoke();

        private readonly Func<SensorGraphViewModel> _resolve;

        public SensorSwitchCandidate(string sensorId, string displayName, Func<SensorGraphViewModel> resolve, bool isDefault = false, Func<double?> yMaxOverride = null)
        {
            SensorId = sensorId;
            DisplayName = displayName;
            _resolve = resolve;
            IsDefault = isDefault;
            _yMaxOverride = yMaxOverride;
        }

        // applies the own y-max here, so it lands on a user switch and on the startup default, which
        // never touches SensorPanelControl
        public SensorGraphViewModel Resolve()
        {
            var graph = _resolve();

            double? yMax = YMaxOverride;
            if (yMax.HasValue) graph.ApplyViewOverrides(null, false, yMax.Value);

            return graph;
        }
    }
}
