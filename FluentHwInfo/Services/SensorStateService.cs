using System;
using System.Collections.Generic;
using FluentHwInfo.Models;

namespace FluentHwInfo.Services
{
    // central in-memory store for everything a user can configure per sensor: visibility,
    // threshold, and widget graph Y-axis scaling
    public class SensorStateService
    {
        // fields
        public static SensorStateService Instance { get; } = new SensorStateService();
        private readonly Dictionary<string, SensorState> _states = new();


        // constructor
        private SensorStateService() { }


        // Public binding surface
        // fires whenever any part of a sensors state changes, so every open view for that sensor can refresh; can fire
        // from any thread, subscribers must marshal to their own UI thread
        public event Action<string, SensorState> StateChanged;
        // returns a fresh default state if none has been configured yet; never null
        public SensorState GetState(string sensorId)
        {
            return _states.TryGetValue(sensorId, out var state) ? state : new SensorState();
        }
        public void SetState(string sensorId, SensorState state)
        {
            _states[sensorId] = state;
            StateChanged?.Invoke(sensorId, state);
            PersistenceService.Instance.SaveSensorStatesDebounced(_states);
        }
        // convenience helper for the hide/restore flow: flips just the hidden flag without touching that sensors
        // threshold or Y-axis config
        public void SetHidden(string sensorId, bool isHidden)
        {
            var state = GetState(sensorId);
            state.IsHidden = isHidden;
            SetState(sensorId, state);
        }


        // persistence 
        // returns the live dictionary directly; PersistenceService only reads it when its debounce timer fires, so no
        // snapshot copy is needed here
        public void LoadFromDisk(Dictionary<string, SensorState> loaded)
        {
            _states.Clear();
            foreach (var kvp in loaded)
            {
                _states[kvp.Key] = kvp.Value;
            }
        }
    }
}