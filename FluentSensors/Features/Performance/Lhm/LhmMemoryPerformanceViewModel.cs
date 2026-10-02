using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Lhm;


namespace FluentSensors.Features.Performance.Lhm
{
    // memory discovery:
    // the physical and the "Virtual Memory" instance merged into one LhmMemoryInstanceViewModel,
    // without a nav entry of its own
    // derives the y-max helpers RoundedTotalMemory and VirtualMemoryTotal, LHM has no total sensor
    public class LhmMemoryPerformanceViewModel
    {
        // === fields ===

        // created by whichever of the two groups comes first
        private LhmMemoryInstanceViewModel _memory;


        // === constructor ===

        public LhmMemoryPerformanceViewModel()
        {
            Memories = new ObservableCollection<LhmMemoryInstanceViewModel>();

            var tree = LhmHardwareTreeService.Instance;

            foreach (var instance in tree.HardwareGroups)
            {
                if (IsPhysicalMemory(instance)) AttachPhysical(instance);
                else if (IsVirtualMemory(instance)) AttachVirtual(instance);
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
                if (IsPhysicalMemory(instance)) AttachPhysical(instance);
                else if (IsVirtualMemory(instance)) AttachVirtual(instance);
            }
        }


        // === private helpers ===

        private static bool IsPhysicalMemory(LhmHardwareInstance instance) =>
            instance.Kind == HardwareGroupKind.Ram && instance.HardwareName == "Total Memory";

        // LHM reports the commit charge as a Ram instance "Virtual Memory" with the same "Memory Used" and "Memory
        // Available"; roughly the Task Manager "Committed"
        private static bool IsVirtualMemory(LhmHardwareInstance instance) =>
            instance.Kind == HardwareGroupKind.Ram && instance.HardwareName == "Virtual Memory";

        private LhmMemoryInstanceViewModel GetOrCreateMemory(string hardwareName)
        {
            if (_memory == null)
            {
                _memory = new LhmMemoryInstanceViewModel(hardwareName);
                Memories.Add(_memory);
            }
            return _memory;
        }

        private void AttachPhysical(LhmHardwareInstance instance)
        {
            var memory = GetOrCreateMemory(instance.HardwareName);

            foreach (var entry in instance.Sensors)
            {
                OnPhysicalSensorDiscovered(memory, entry, instance);
            }
            instance.Sensors.CollectionChanged += (s, e) => OnPhysicalSensorsChanged(memory, e, instance);
        }

        private void AttachVirtual(LhmHardwareInstance instance)
        {
            var memory = GetOrCreateMemory(instance.HardwareName);

            foreach (var entry in instance.Sensors)
            {
                OnVirtualSensorDiscovered(memory, entry, instance);
            }
            instance.Sensors.CollectionChanged += (s, e) => OnVirtualSensorsChanged(memory, e, instance);
        }

        private void OnPhysicalSensorsChanged(LhmMemoryInstanceViewModel memory, NotifyCollectionChangedEventArgs e, LhmHardwareInstance instance)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnPhysicalSensorDiscovered(memory, entry, instance);
            }
        }

        private void OnVirtualSensorsChanged(LhmMemoryInstanceViewModel memory, NotifyCollectionChangedEventArgs e, LhmHardwareInstance instance)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnVirtualSensorDiscovered(memory, entry, instance);
            }
        }

        private void OnPhysicalSensorDiscovered(LhmMemoryInstanceViewModel memory, LhmSensorEntry entry, LhmHardwareInstance instance)
        {
            if (entry.Name == "Memory Used")
            {
                memory.Used = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(memory.Used, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(memory.Used, entry, e);
                entry.PropertyChanged += (s, e) => RecalculateRoundedTotal(memory, instance, e);
                RecalculateRoundedTotal(memory, instance, null);
            }
            else if (entry.Name == "Memory Available")
            {
                memory.Available = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(memory.Available, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(memory.Available, entry, e);
                entry.PropertyChanged += (s, e) => RecalculateRoundedTotal(memory, instance, e);
                RecalculateRoundedTotal(memory, instance, null);
            }
        }

        private void OnVirtualSensorDiscovered(LhmMemoryInstanceViewModel memory, LhmSensorEntry entry, LhmHardwareInstance instance)
        {
            if (entry.Name == "Memory Used")
            {
                memory.VirtualMemoryUsed = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(memory.VirtualMemoryUsed, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(memory.VirtualMemoryUsed, entry, e);
                entry.PropertyChanged += (s, e) => RecalculateVirtualTotal(memory, instance, e);
                RecalculateVirtualTotal(memory, instance, null);
            }
            else if (entry.Name == "Memory Available")
            {
                entry.PropertyChanged += (s, e) => RecalculateVirtualTotal(memory, instance, e);
                RecalculateVirtualTotal(memory, instance, null);
            }
        }

        // Used + Available up to the next 4 GB, on every physical update
        private void RecalculateRoundedTotal(LhmMemoryInstanceViewModel memory, LhmHardwareInstance instance, PropertyChangedEventArgs e)
        {
            if (e != null && e.PropertyName != nameof(LhmSensorEntry.Value)) return;

            var used = instance.Sensors.FirstOrDefault(s => s.Name == "Memory Used");
            var available = instance.Sensors.FirstOrDefault(s => s.Name == "Memory Available");
            if (used == null || available == null) return;

            memory.RoundedTotalMemory = Math.Ceiling((used.Value + available.Value) / 4.0) * 4.0;
        }

        // the same for virtual memory, unrounded
        private void RecalculateVirtualTotal(LhmMemoryInstanceViewModel memory, LhmHardwareInstance instance, PropertyChangedEventArgs e)
        {
            if (e != null && e.PropertyName != nameof(LhmSensorEntry.Value)) return;

            var used = instance.Sensors.FirstOrDefault(s => s.Name == "Memory Used");
            var available = instance.Sensors.FirstOrDefault(s => s.Name == "Memory Available");
            if (used == null || available == null) return;

            memory.VirtualMemoryTotal = used.Value + available.Value;
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
