using System;
using System.Collections.Generic;

using FluentSensors.Common.Sensors;
using FluentSensors.Persistence.Models;


namespace FluentSensors.Persistence.Services
{
    // central in-memory store for the three ordered sensor selection profiles (widget window, csv, taskbar)
    // pure selection storage, knows nothing about checkboxes, ViewModels, or windows
    public class SensorSelectionService
    {
        // === fields ===

        private SensorSelectionState _state = new();


        // === singleton instance ===

        public static SensorSelectionService Instance { get; } = new SensorSelectionService();


        // === constructor ===

        private SensorSelectionService() { }


        // === public api ===

        // returns the live list directly, callers must not mutate it, use SetMembership so changes actually persist
        public IReadOnlyList<string> GetSelection(SensorSelectionProfile profile) => GetList(profile);

        public bool IsSelected(SensorSelectionProfile profile, string sensorId) => GetList(profile).Contains(sensorId);

        // adds or removes a single sensor from a profiles selection, called live on every checkbox toggle rather than
        // in a batch, so persistence never depends on some separate commit step happening afterward
        // a newly added sensor is appended at the end, the existing order of everything already in the list is left
        // untouched
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

        // one-time migration from the pre-profile widget pin list (WindowState "Widget" PinnedSensorIds)
        // no-ops once HasMigratedLegacyWidgetSelection is set, regardless of what WidgetWindow contains by then, so a
        // user who later empties their widget selection on purpose never gets the old list silently brought back
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
