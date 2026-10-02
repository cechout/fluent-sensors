using System;
using System.Collections.Generic;

using FluentSensors.Common.Sensors;
using FluentSensors.Persistence.Models;


namespace FluentSensors.Persistence.Services
{
    // the sensor selections:
    // the three ordered selection profiles in memory (widget window, csv, taskbar); knows nothing of
    // checkboxes, view models or windows
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
        public IReadOnlyList<string> GetSelection(SensorSelectionProfile profile) => GetList(profile);

        public bool IsSelected(SensorSelectionProfile profile, string sensorId) => GetList(profile).Contains(sensorId);

        // one sensor in or out, live on every checkbox toggle, so nothing waits on a commit
        // step; a new one goes to the end
        public void SetMembership(SensorSelectionProfile profile, string sensorId, bool isMember)
        {
            var list = GetList(profile);

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

        private List<string> GetList(SensorSelectionProfile profile) => profile switch
        {
            SensorSelectionProfile.WidgetWindow => _state.WidgetWindow,
            SensorSelectionProfile.Csv => _state.Csv,
            SensorSelectionProfile.Taskbar => _state.Taskbar,
            _ => throw new ArgumentOutOfRangeException(nameof(profile))
        };

        private void Persist()
        {
            PersistenceService.Instance.SaveSensorSelectionsDebounced(_state);
        }
    }
}
