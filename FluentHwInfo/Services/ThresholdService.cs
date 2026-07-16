using System;
using System.Collections.Generic;
using FluentHwInfo.Models;

namespace FluentHwInfo.Services
{
    // central in-memory store for per-sensor threshold config
    // exists so MainWindow and WidgetWindow can share threshold state without either one knowing the other exists
    public class ThresholdService
    {
        // fields
        public static ThresholdService Instance { get; } = new ThresholdService();
        private readonly Dictionary<string, SensorThreshold> _thresholds = new();


        // constructor
        private ThresholdService() { }

        // Public binding surface
        // fires whenever a threshold changes, so every open view for that sensor can refresh
        // note: subscribers are responsible for marshalling to their own UI thread
        public event Action<string, SensorThreshold> ThresholdChanged;

        // returns null if no threshold has been set for this sensor yet
        public SensorThreshold GetThreshold(string sensorId)
        {
            return _thresholds.TryGetValue(sensorId, out var threshold) ? threshold : null;
        }

        public void SetThreshold(string sensorId, SensorThreshold threshold)
        {
            _thresholds[sensorId] = threshold;
            ThresholdChanged?.Invoke(sensorId, threshold);
        }
    }
}