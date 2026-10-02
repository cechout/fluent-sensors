using System.Collections.Generic;

using FluentSensors.Persistence.Models;


namespace FluentSensors.Persistence.Services
{
    // the window states:
    // position and size in memory, keyed by a fixed window name ("Main", "Widget"); a plain
    // store like SensorStateService
    public class WindowStateService
    {
        // === fields ===

        private readonly Dictionary<string, WindowState> _states = new();


        // === singleton instance ===

        public static WindowStateService Instance { get; } = new WindowStateService();


        // === constructor ===

        private WindowStateService() { }


        // === public api ===

        // null for a window never saved
        public WindowState GetState(string windowKey)
        {
            return _states.TryGetValue(windowKey, out var state) ? state : null;
        }

        public void SetState(string windowKey, WindowState state)
        {
            _states[windowKey] = state;
            PersistenceService.Instance.SaveWindowStatesDebounced(_states);
        }

        // persistence
        public void LoadFromDisk(Dictionary<string, WindowState> loaded)
        {
            _states.Clear();
            foreach (var kvp in loaded)
            {
                _states[kvp.Key] = kvp.Value;
            }
        }
    }
}
