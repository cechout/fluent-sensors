using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Lhm;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance.Lhm
{
    // discovers every storage drive instance from LhmHardwareTreeService and creates one LhmStorageInstanceViewModel
    // per drive; parses each raw LHM sensor into the right property on the right instance
    // The instance itself stays a dumb data holder
    public class LhmStoragePerformanceViewModel
    {
        // === constructor ===

        public LhmStoragePerformanceViewModel()
        {
            Drives = new ObservableCollection<LhmStorageInstanceViewModel>();

            var tree = LhmHardwareTreeService.Instance;

            foreach (var instance in tree.HardwareGroups)
            {
                if (instance.Kind == HardwareGroupKind.Storage) AttachToInstance(instance);
            }
            tree.HardwareGroups.CollectionChanged += OnTreeHardwareGroupsChanged;
        }


        // === bindable properties ===

        public ObservableCollection<LhmStorageInstanceViewModel> Drives { get; }


        // === event handlers ===

        private void OnTreeHardwareGroupsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmHardwareInstance instance in e.NewItems)
            {
                if (instance.Kind == HardwareGroupKind.Storage) AttachToInstance(instance);
            }
        }


        // === private helpers ===

        private void AttachToInstance(LhmHardwareInstance instance)
        {
            var drive = new LhmStorageInstanceViewModel(instance.HardwareName);
            Drives.Add(drive);

            foreach (var entry in instance.Sensors)
            {
                OnSensorDiscovered(drive, entry);
            }
            ApplyCategoryFallbacks(drive);
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(drive, e);
        }

        // falls back to the first candidate if a persisted choice never shows up on this system (removed hardware,
        // imported state from another PC); otherwise that category would stay without an active graph forever
        private static void ApplyCategoryFallbacks(LhmStorageInstanceViewModel drive)
        {
            if (drive.TotalActivity == null && drive.TotalActivityOptions.Count > 0) drive.SetTotalActivityWithoutPersisting(drive.TotalActivityOptions[0].Resolve());
            if (drive.ReadRate == null && drive.ReadRateOptions.Count > 0) drive.SetReadRateWithoutPersisting(drive.ReadRateOptions[0].Resolve());
            if (drive.WriteRate == null && drive.WriteRateOptions.Count > 0) drive.SetWriteRateWithoutPersisting(drive.WriteRateOptions[0].Resolve());
        }

        private void OnInstanceSensorsChanged(LhmStorageInstanceViewModel drive, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnSensorDiscovered(drive, entry);
            }
        }

        private void OnSensorDiscovered(LhmStorageInstanceViewModel drive, LhmSensorEntry entry)
        {
            switch (entry.Name)
            {
                case "Total Activity":
                    RegisterCategoryCandidate(drive, "TotalActivity", entry,
                        d => d.TotalActivity, (d, v) => d.SetTotalActivityWithoutPersisting(v), drive.TotalActivityOptions);
                    break;

                case "Free Space":
                    RegisterCategoryCandidate(drive, "TotalActivity", entry,
                        d => d.TotalActivity, (d, v) => d.SetTotalActivityWithoutPersisting(v), drive.TotalActivityOptions);
                    break;

                case "Total Space":
                    drive.TotalSpace = entry.Value;
                    entry.PropertyChanged += (s, e) => OnTotalSpaceChanged(drive, entry, e);
                    break;

                case "Write Rate":
                    RegisterCategoryCandidate(drive, "Write", entry,
                        d => d.WriteRate, (d, v) => d.SetWriteRateWithoutPersisting(v), drive.WriteRateOptions);
                    break;

                case "Write Activity":
                    RegisterCategoryCandidate(drive, "Write", entry,
                        d => d.WriteRate, (d, v) => d.SetWriteRateWithoutPersisting(v), drive.WriteRateOptions);
                    break;

                case "Read Rate":
                    RegisterCategoryCandidate(drive, "Read", entry,
                        d => d.ReadRate, (d, v) => d.SetReadRateWithoutPersisting(v), drive.ReadRateOptions);
                    break;

                case "Read Activity":
                    RegisterCategoryCandidate(drive, "Read", entry,
                        d => d.ReadRate, (d, v) => d.SetReadRateWithoutPersisting(v), drive.ReadRateOptions);
                    break;
            }
        }

        // adds entry as a candidate, and activates it if nothing is active yet and it matches the persisted choice
        // (or nothing was ever persisted, first-found-wins)
        // guard: OnSensorDiscovered can run again for a sensor already registered here (e.g. re-triggered via
        // instance.Sensors.CollectionChanged); without it every rerun would add a duplicate candidate
        private void RegisterCategoryCandidate(
            LhmStorageInstanceViewModel drive,
            string category,
            LhmSensorEntry entry,
            Func<LhmStorageInstanceViewModel, SensorGraphViewModel> getActive,
            Action<LhmStorageInstanceViewModel, SensorGraphViewModel> setActiveWithoutPersisting,
            ObservableCollection<SensorSwitchCandidate> options)
        {
            if (options.Any(c => c.SensorId == entry.Id)) return;

            SensorGraphViewModel cached = null;
            SensorGraphViewModel Resolve()
            {
                if (cached != null) return cached;
                cached = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(cached, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(cached, entry, e);
                return cached;
            }

            options.Add(new SensorSwitchCandidate(entry.Id, entry.Name, Resolve));

            if (getActive(drive) != null) return; // already resolved, this is just an additional alternative

            string persistedId = SensorSwitchStateService.Instance.GetSelectedSensorId(drive.HardwareName, category);
            if (persistedId == entry.Id || persistedId == null)
            {
                setActiveWithoutPersisting(drive, Resolve());
            }
        }

        private static void OnEntryValueChanged(SensorGraphViewModel graph, LhmSensorEntry entry, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LhmSensorEntry.Value)) return;
            PushDataPoint(graph, entry);
        }

        private static void OnTotalSpaceChanged(LhmStorageInstanceViewModel drive, LhmSensorEntry entry, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LhmSensorEntry.Value)) return;
            drive.TotalSpace = entry.Value;
        }

        private static void PushDataPoint(SensorGraphViewModel graph, LhmSensorEntry entry)
        {
            graph.AddDataPoint(entry.Value, SensorUnitFormatter.Format(entry.Value, entry.SensorType));
        }
    }
}
