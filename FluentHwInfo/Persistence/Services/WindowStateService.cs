using System.Collections.Generic;

using FluentHwInfo.Persistence.Models;


namespace FluentHwInfo.Persistence.Services
{
    // central in-memory store for window position and size, keyed by a fixed window identifier ("Main", "Widget")
    // same dumb-store pattern as SensorStateService
    public class WindowStateService
    {
        // === fields ===

        private readonly Dictionary<string, WindowState> _states = new();


        // === singleton instance ===

        public static WindowStateService Instance { get; } = new WindowStateService();


        // === constructor ===

        private WindowStateService() { }


        // === public api ===

        // returns null if this window has never been positioned/saved before
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