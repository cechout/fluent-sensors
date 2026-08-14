using System;


namespace FluentSensors.Controls.SensorGraph
{
    // one selectable option in a SensorPanelControls sensor-switch combobox
    // Resolve lazily builds the real graph on first pick, and caches it after that
    public class SensorSwitchCandidate
    {
        public string SensorId { get; }
        public string DisplayName { get; }

        // marks this candidate as the categorys default pick when nothing was persisted yet; set explicitly per
        // candidate at registration, independent of discovery order
        public bool IsDefault { get; }

        // optional per-candidate Y-axis max; null means the panels own ManualYMaxOverride applies as before
        // a Func, not a plain value, so a source that only arrives later during discovery (e.g. a drives Total
        // Space) is still read live once this candidate actually becomes active, regardless of arrival order
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

        // resolves the graph and, if this candidate carries its own Y-axis max, applies it right here; done in
        // Resolve rather than the switch handler so it lands whether the candidate becomes active via a user switch
        // or via the startup default activation, which never touches SensorPanelControl
        public SensorGraphViewModel Resolve()
        {
            var graph = _resolve();

            double? yMax = YMaxOverride;
            if (yMax.HasValue) graph.ApplyViewOverrides(null, false, yMax.Value);

            return graph;
        }
    }
}
