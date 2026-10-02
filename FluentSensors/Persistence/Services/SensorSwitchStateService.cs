using System.Collections.Generic;

using FluentSensors.Persistence.Models;


namespace FluentSensors.Persistence.Services
{
    // the switch states:
    // the active sensor per switchable slot, keyed by hardware instance and category, not sensor id
    public class SensorSwitchStateService
    {
        // === fields ===

        private readonly Dictionary<string, SensorSwitchState> _states = new();


        // === singleton instance ===

        public static SensorSwitchStateService Instance { get; } = new SensorSwitchStateService();


        // === constructor ===

        private SensorSwitchStateService() { }


        // === public api ===

        // null while the slot shows its default
        public string GetSelectedSensorId(string hardwareName, string category)
        {
            return _states.TryGetValue(BuildKey(hardwareName, category), out var state) ? state.SelectedSensorId : null;
        }

        public void SetSelectedSensorId(string hardwareName, string category, string sensorId)
        {
            string key = BuildKey(hardwareName, category);
            _states[key] = new SensorSwitchState { SelectedSensorId = sensorId };
            // the live dictionary; PersistenceService reads it only when the debounce fires, so no copy
            PersistenceService.Instance.SaveSensorSwitchStatesDebounced(_states);
        }

        // persistence
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
