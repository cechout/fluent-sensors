using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Lhm;


namespace FluentSensors.Features.Performance.Lhm
{
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
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(drive, e);
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
                case "Write Rate":
                    drive.WriteRate = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(drive.WriteRate, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(drive.WriteRate, entry, e);
                    break;

                case "Read Rate":
                    drive.ReadRate = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(drive.ReadRate, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(drive.ReadRate, entry, e);
                    break;
            }
        }

        private static void OnEntryValueChanged(SensorGraphViewModel graph, LhmSensorEntry entry, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LhmSensorEntry.Value)) return;
            PushDataPoint(graph, entry);
        }

        private static void PushDataPoint(SensorGraphViewModel graph, LhmSensorEntry entry)
        {
            graph.AddDataPoint(entry.Value, SensorUnitFormatter.Format(entry.Value, entry.SensorType));
        }
    }
}