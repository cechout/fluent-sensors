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
    // storage discovery:
    // one LhmStorageInstanceViewModel per drive in LhmHardwareTreeService, every raw LHM sensor parsed into its
    // property; the instance stays a data holder
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

        // once after the first sensor batch, per category: without a saved choice the IsDefault candidate beats
        // discovery order; with nothing active, the first candidate
        private static void ApplyCategoryFallbacks(LhmStorageInstanceViewModel drive)
        {
            ActivateDefault(drive.HardwareName, "TotalActivity", drive.TotalActivityOptions, () => drive.TotalActivity, drive.SetTotalActivityWithoutPersisting);
            ActivateDefault(drive.HardwareName, "Read", drive.ReadRateOptions, () => drive.ReadRate, drive.SetReadRateWithoutPersisting);
            ActivateDefault(drive.HardwareName, "Write", drive.WriteRateOptions, () => drive.WriteRate, drive.SetWriteRateWithoutPersisting);
        }

        private static void ActivateDefault(
            string hardwareName, string category, ObservableCollection<SensorSwitchCandidate> options,
            Func<SensorGraphViewModel> getActive, Action<SensorGraphViewModel> setActiveWithoutPersisting)
        {
            if (options.Count == 0) return;

            if (SensorSwitchStateService.Instance.GetSelectedSensorId(hardwareName, category) == null)
            {
                var flaggedDefault = options.FirstOrDefault(c => c.IsDefault);
                if (flaggedDefault != null)
                {
                    var active = getActive();
                    bool activeIsAlreadyDefault = active != null && options.Any(c => c.SensorId == active.SensorId && c.IsDefault);
                    if (!activeIsAlreadyDefault)
                    {
                        setActiveWithoutPersisting(flaggedDefault.Resolve());
                        return;
                    }
                }
            }

            if (getActive() == null) setActiveWithoutPersisting(options[0].Resolve());
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
                        d => d.TotalActivity, (d, v) => d.SetTotalActivityWithoutPersisting(v), drive.TotalActivityOptions, isDefault: true);
                    break;

                case "Free Space":
                    // scaled to the drive capacity; a live Func, Total Space may come later
                    RegisterCategoryCandidate(drive, "TotalActivity", entry,
                        d => d.TotalActivity, (d, v) => d.SetTotalActivityWithoutPersisting(v), drive.TotalActivityOptions,
                        yMaxOverride: () => drive.TotalSpace > 0 ? drive.TotalSpace : (double?)null);
                    break;

                case "Total Space":
                    drive.TotalSpace = entry.Value;
                    entry.PropertyChanged += (s, e) => OnTotalSpaceChanged(drive, entry, e);
                    break;

                case "Write Rate":
                    RegisterCategoryCandidate(drive, "Write", entry,
                        d => d.WriteRate, (d, v) => d.SetWriteRateWithoutPersisting(v), drive.WriteRateOptions, isDefault: true);
                    break;

                case "Write Activity":
                    RegisterCategoryCandidate(drive, "Write", entry,
                        d => d.WriteRate, (d, v) => d.SetWriteRateWithoutPersisting(v), drive.WriteRateOptions, yMaxOverride: () => 100);
                    break;

                case "Read Rate":
                    RegisterCategoryCandidate(drive, "Read", entry,
                        d => d.ReadRate, (d, v) => d.SetReadRateWithoutPersisting(v), drive.ReadRateOptions, isDefault: true);
                    break;

                case "Read Activity":
                    RegisterCategoryCandidate(drive, "Read", entry,
                        d => d.ReadRate, (d, v) => d.SetReadRateWithoutPersisting(v), drive.ReadRateOptions, yMaxOverride: () => 100);
                    break;
            }
        }

        // adds a candidate, active when nothing is yet and it is the saved choice (or none was saved, first found;
        // ApplyCategoryFallbacks corrects to the flagged default)
        // guard; OnSensorDiscovered can rerun for a registered sensor (instance.Sensors.CollectionChanged), which
        // must not add a second candidate
        private void RegisterCategoryCandidate(
            LhmStorageInstanceViewModel drive,
            string category,
            LhmSensorEntry entry,
            Func<LhmStorageInstanceViewModel, SensorGraphViewModel> getActive,
            Action<LhmStorageInstanceViewModel, SensorGraphViewModel> setActiveWithoutPersisting,
            ObservableCollection<SensorSwitchCandidate> options,
            bool isDefault = false,
            Func<double?> yMaxOverride = null)
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

            options.Add(new SensorSwitchCandidate(entry.Id, entry.Name, Resolve, isDefault, yMaxOverride));

            if (getActive(drive) != null) return; // resolved, one more alternative

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
