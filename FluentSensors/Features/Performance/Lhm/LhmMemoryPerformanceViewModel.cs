using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core;


namespace FluentSensors.Features.Performance.Lhm
{
    public class LhmMemoryPerformanceViewModel : INotifyPropertyChanged
    {
        // === constructor ===

        public LhmMemoryPerformanceViewModel()
        {
            var tree = LhmHardwareTreeService.Instance;

            foreach (var instance in tree.HardwareGroups)
            {
                if (IsPhysicalMemory(instance)) AttachToInstance(instance);
            }
            tree.HardwareGroups.CollectionChanged += OnTreeHardwareGroupsChanged;
        }


        // === bindable properties ===

        // LHMs raw hardware name for this group (currently always "Total Memory"); captured once, used by Performance-
        // ViewModel to populate the RAM nav items DisplayName
        private string _hardwareName;
        public string HardwareName
        {
            get => _hardwareName;
            private set { _hardwareName = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _used;
        public SensorGraphViewModel Used
        {
            get => _used;
            private set { _used = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _available;
        public SensorGraphViewModel Available
        {
            get => _available;
            private set { _available = value; OnPropertyChanged(); }
        }


        // === event handlers ===

        private void OnTreeHardwareGroupsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmHardwareInstance instance in e.NewItems)
            {
                if (IsPhysicalMemory(instance)) AttachToInstance(instance);
            }
        }


        // === private helpers ===

        // LHM reports "Virtual Memory" (commit charge incl. page file) as a separate Memory-kind instance; we only want
        // the physical RAM group here
        private static bool IsPhysicalMemory(LhmHardwareInstance instance)
        {
            return instance.Kind == HardwareGroupKind.Ram && instance.HardwareName == "Total Memory";
        }

        private void AttachToInstance(LhmHardwareInstance instance)
        {
            if (HardwareName == null) HardwareName = instance.HardwareName;

            foreach (var entry in instance.Sensors)
            {
                OnSensorDiscovered(entry);
            }
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(e);
        }

        private void OnInstanceSensorsChanged(NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnSensorDiscovered(entry);
            }
        }

        private void OnSensorDiscovered(LhmSensorEntry entry)
        {
            if (entry.Name == "Memory Used")
            {
                Used = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(Used, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(Used, entry, e);
            }
            else if (entry.Name == "Memory Available")
            {
                Available = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(Available, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(Available, entry, e);
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


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}