using System.Collections.Generic;

using FluentSensors.Persistence.Models;


namespace FluentSensors.Persistence.Services
{
    // which sensor is active per switchable category slot, keyed by hardware instance + category rather than sensor id
    public class SensorSwitchStateService
    {
        // === fields ===

        private readonly Dictionary<string, SensorSwitchState> _states = new();


        // === singleton instance ===

        public static SensorSwitchStateService Instance { get; } = new SensorSwitchStateService();


        // === constructor ===

        private SensorSwitchStateService() { }


        // === public api ===

        // null means this slot was never switched away from its default
        public string GetSelectedSensorId(string hardwareName, string category)
        {
            return _states.TryGetValue(BuildKey(hardwareName, category), out var state) ? state.SelectedSensorId : null;
        }

        public void SetSelectedSensorId(string hardwareName, string category, string sensorId)
        {
            string key = BuildKey(hardwareName, category);
            _states[key] = new SensorSwitchState { SelectedSensorId = sensorId };
            PersistenceService.Instance.SaveSensorSwitchStatesDebounced(_states);
        }

        // persistence
        // returns the live dictionary directly; PersistenceService only reads it when its debounce timer fires, so no
        // snapshot copy is needed here
        public void LoadFromDisk(Dictionary<string, SensorSwitchState> loaded)
        {
            _states.Clear();
            foreach (var kvp in loaded)
            {
                _states[kvp.Key] = kvp.Value;
            }
        }


        // === private helpers ===

        private static string BuildKey(string hardwareName, string category) => $"{hardwareName}|{category}";
    }
}
