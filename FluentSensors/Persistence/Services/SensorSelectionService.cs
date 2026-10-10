using System;
using System.Collections.Generic;

using FluentSensors.Common.Sensors;
using FluentSensors.Persistence.Models;


namespace FluentSensors.Persistence.Services
{
    // the sensor selections:
    // the three ordered selection profiles in memory (widget windows, csv, taskbar); knows nothing of
    // checkboxes, view models or windows
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

        // persistence
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
