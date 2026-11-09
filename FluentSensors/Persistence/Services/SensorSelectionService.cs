using System;
using System.Collections.Generic;
using System.Linq;

using FluentSensors.Common.Sensors;
using FluentSensors.Persistence.Models;


namespace FluentSensors.Persistence.Services
{
    // the sensor selections:
    // the ordered selection profiles in memory:
    // - widget windows
    // - csv
    // - taskbar
    // - widgets board
    // knows nothing of checkboxes, view models or windows
    // widgetIndex picks the widget window (0 based) and is ignored by the other profiles
    public class SensorSelectionService
    {
        // === fields ===

        private SensorSelectionState _state = new();


        // === singleton instance ===

        public static SensorSelectionService Instance { get; } = new SensorSelectionService();


        // === constructor ===

        private SensorSelectionService() { }


        // === public api ===

        // the live list; read only, changes go through SetMembership so they persist
        public IReadOnlyList<string> GetSelection(SensorSelectionProfile profile, int widgetIndex = 0) =>
            GetList(profile, widgetIndex);

        public bool IsSelected(SensorSelectionProfile profile, string sensorId, int widgetIndex = 0) =>
            GetList(profile, widgetIndex).Contains(sensorId);

        public bool TaskbarSensorsDefaultToTray => _state.TaskbarSensorsDefaultToTray;

        public event Action TaskbarSensorPlacementChanged;

        public IReadOnlyList<string> TaskbarSensorOrder => _state.TaskbarSensorOrder ??= new List<string>();

        // Keep preferences for disconnected sensors without changing selection membership or placement.
        public void SetTaskbarSensorOrder(IEnumerable<string> sensorIds)
        {
            var order = sensorIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            order.AddRange(TaskbarSensorOrder.Where(id => !order.Contains(id)));
            if (TaskbarSensorOrder.SequenceEqual(order)) return;
            _state.TaskbarSensorOrder = order;
            Persist();
            TaskbarSensorPlacementChanged?.Invoke();
        }

        public bool IsCompactTaskbarSensor(string sensorId) => _state.CompactTaskbarSensors?.Contains(sensorId) == true;

        public void ToggleCompactTaskbarSensor(string sensorId)
        {
            _state.CompactTaskbarSensors ??= new HashSet<string>();
            if (!_state.CompactTaskbarSensors.Add(sensorId)) _state.CompactTaskbarSensors.Remove(sensorId);
            Persist();
            TaskbarSensorPlacementChanged?.Invoke();
        }

        public bool IsTaskbarSensorInTray(string sensorId) =>
            _state.TaskbarSensorTrayOverrides != null && _state.TaskbarSensorTrayOverrides.TryGetValue(sensorId, out bool inTray)
                ? inTray : _state.TaskbarSensorsDefaultToTray;

        public void SetTaskbarSensorsDefaultToTray(bool inTray)
        {
            if (_state.TaskbarSensorsDefaultToTray == inTray) return;
            _state.TaskbarSensorsDefaultToTray = inTray;
            Persist();
            TaskbarSensorPlacementChanged?.Invoke();
        }

        public void SetTaskbarSensorInTray(string sensorId, bool inTray)
        {
            _state.TaskbarSensorTrayOverrides ??= new Dictionary<string, bool>();
            if (inTray == _state.TaskbarSensorsDefaultToTray)
                _state.TaskbarSensorTrayOverrides.Remove(sensorId);
            else
                _state.TaskbarSensorTrayOverrides[sensorId] = inTray;
            Persist();
            TaskbarSensorPlacementChanged?.Invoke();
        }

        // one sensor in or out, live on every checkbox toggle, so nothing waits on a commit
        // step; a new one goes to the end
        public void SetMembership(SensorSelectionProfile profile, string sensorId, bool isMember, int widgetIndex = 0)
        {
            var list = GetList(profile, widgetIndex);

            if (isMember)
            {
                if (list.Contains(sensorId)) return;
                list.Add(sensorId);
            }
            else
            {
                if (!list.Remove(sensorId)) return;
            }

            Persist();
        }

        // one-time migration from the widget pin list (WindowState "Widget" PinnedSensorIds); after
        // HasMigratedLegacyWidgetSelection a deliberately emptied selection stays empty
        public void MigrateFromLegacyWidgetPins(List<string> legacyPinnedSensorIds)
        {
            if (_state.HasMigratedLegacyWidgetSelection) return;

            _state.WidgetWindow = legacyPinnedSensorIds != null ? new List<string>(legacyPinnedSensorIds) : new List<string>();
            _state.HasMigratedLegacyWidgetSelection = true;
            Persist();
        }

        // replaces the selections with what PersistenceService read from disk, once at startup
        public void LoadFromDisk(SensorSelectionState loaded)
        {
            _state = loaded ?? new SensorSelectionState();
        }


        // === private helpers ===

        private List<string> GetList(SensorSelectionProfile profile, int widgetIndex) => profile switch
        {
            SensorSelectionProfile.WidgetWindow => GetWidgetList(widgetIndex),
            SensorSelectionProfile.Csv => _state.Csv,
            SensorSelectionProfile.Taskbar => _state.Taskbar,
            SensorSelectionProfile.WidgetBoard => _state.WidgetBoard ??= new List<string>(),
            _ => throw new ArgumentOutOfRangeException(nameof(profile))
        };

        // a window without a list yet gets an empty one
        private List<string> GetWidgetList(int widgetIndex)
        {
            if (widgetIndex <= 0) return _state.WidgetWindow;

            _state.ExtraWidgetWindows ??= new List<List<string>>();
            while (_state.ExtraWidgetWindows.Count < widgetIndex)
            {
                _state.ExtraWidgetWindows.Add(new List<string>());
            }

            return _state.ExtraWidgetWindows[widgetIndex - 1] ??= new List<string>();
        }

        private void Persist()
        {
            PersistenceService.Instance.SaveSensorSelectionsDebounced(_state);
        }
    }
}
