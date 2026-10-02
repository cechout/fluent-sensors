using System;
using System.Collections.Generic;

using FluentSensors.Persistence.Models;


namespace FluentSensors.Persistence.Services
{
    // the sensor states:
    // everything configurable per sensor, in memory: visibility, threshold and the y-axis scaling per graph scope
    public class SensorStateService
    {
        // === fields ===

        private readonly Dictionary<string, SensorState> _states = new();


        // === singleton instance ===

        public static SensorStateService Instance { get; } = new SensorStateService();


        // === constructor ===

        private SensorStateService() { }


        // === public api ===

        // a fresh default when none is configured, never null
        public SensorState GetState(string sensorId)
        {
            return _states.TryGetValue(sensorId, out var state) ? state : new SensorState();
        }

        public void SetState(string sensorId, SensorState state)
        {
            _states[sensorId] = state;
            StateChanged?.Invoke(sensorId, state);
            // the live dictionary; PersistenceService reads it only when the debounce fires, so no copy
            PersistenceService.Instance.SaveSensorStatesDebounced(_states);
        }

        // for hide and restore, the hidden flag only
        public void SetHidden(string sensorId, bool isHidden)
        {
            var state = GetState(sensorId);
            state.IsHidden = isHidden;
            SetState(sensorId, state);
        }

        // the threshold value and the manual y-max of every scope back to the type defaults, on a data unit switch
        // where a kept value turns odd (50 Mbit/s reads as 6 MB/s)
        // the switches stay: threshold on or off, direction, color, auto scaling, visibility
        // raises StateChanged for an unconfigured sensor too, so open editors re-resolve the default; only
        // a configured one is written
        public void ResetUnitDependentValues(string sensorId)
        {
            if (!_states.TryGetValue(sensorId, out var state))
            {
                StateChanged?.Invoke(sensorId, new SensorState());
                return;
            }

            state.Threshold.Value = null;
            state.PerformanceYAxis.ManualYMax = null;
            state.WidgetYAxis.ManualYMax = null;
            state.TaskbarYAxis.ManualYMax = null;
            SetState(sensorId, state);
        }

        // persistence
        public void LoadFromDisk(Dictionary<string, SensorState> loaded)
        {
            _states.Clear();
            foreach (var kvp in loaded)
            {
                _states[kvp.Key] = kvp.Value;
            }
        }


        // === events ===

        // any change, for every open view of the sensor; from any thread, subscribers marshal themselves
        public event Action<string, SensorState> StateChanged;
    }
}
