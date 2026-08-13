using System;


namespace FluentSensors.Controls.SensorGraph
{
    // one selectable option in a SensorPanelControls sensor-switch combobox
    // Resolve lazily builds the real graph on first pick, and caches it after that
    public class SensorSwitchCandidate
    {
        public string SensorId { get; }
        public string DisplayName { get; }

        private readonly Func<SensorGraphViewModel> _resolve;

        public SensorSwitchCandidate(string sensorId, string displayName, Func<SensorGraphViewModel> resolve)
        {
            SensorId = sensorId;
            DisplayName = displayName;
            _resolve = resolve;
        }

        public SensorGraphViewModel Resolve() => _resolve();
    }
}
