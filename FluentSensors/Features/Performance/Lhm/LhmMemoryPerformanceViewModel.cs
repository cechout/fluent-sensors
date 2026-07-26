using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Lhm;


namespace FluentSensors.Features.Performance.Lhm
{
    public class LhmMemoryPerformanceViewModel
    {
        // === constructor ===

        public LhmMemoryPerformanceViewModel()
        {
            Memories = new ObservableCollection<LhmMemoryInstanceViewModel>();

            var tree = LhmHardwareTreeService.Instance;

            foreach (var instance in tree.HardwareGroups)
            {
                if (IsPhysicalMemory(instance)) AttachToInstance(instance);
            }
            tree.HardwareGroups.CollectionChanged += OnTreeHardwareGroupsChanged;
        }


        // === bindable properties ===

        public ObservableCollection<LhmMemoryInstanceViewModel> Memories { get; }


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

        // LHM reports "Virtual Memory" (commit charge incl. page file) as a separate Ram-kind instance;
        // we only want the physical RAM group(s) here
        private static bool IsPhysicalMemory(LhmHardwareInstance instance)
        {
            return instance.Kind == HardwareGroupKind.Ram && instance.HardwareName == "Total Memory";
        }

        private void AttachToInstance(LhmHardwareInstance instance)
        {
            var memory = new LhmMemoryInstanceViewModel(instance.HardwareName);
            Memories.Add(memory);

            foreach (var entry in instance.Sensors)
            {
                OnSensorDiscovered(memory, entry);
            }
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(memory, e);
        }

        private void OnInstanceSensorsChanged(LhmMemoryInstanceViewModel memory, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnSensorDiscovered(memory, entry);
            }
        }

        private void OnSensorDiscovered(LhmMemoryInstanceViewModel memory, LhmSensorEntry entry)
        {
            if (entry.Name == "Memory Used")
            {
                memory.Used = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(memory.Used, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(memory.Used, entry, e);
            }
            else if (entry.Name == "Memory Available")
            {
                memory.Available = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(memory.Available, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(memory.Available, entry, e);
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