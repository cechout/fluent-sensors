using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core;


namespace FluentSensors.Features.Performance.Lhm
{
    public class LhmCpuPerformanceViewModel
    {
        // === constructor ===

        public LhmCpuPerformanceViewModel()
        {
            Cpus = new ObservableCollection<LhmCpuInstanceViewModel>();

            var tree = LhmHardwareTreeService.Instance;

            foreach (var instance in tree.HardwareGroups)
            {
                if (instance.Kind == HardwareGroupKind.Cpu) AttachToInstance(instance);
            }
            tree.HardwareGroups.CollectionChanged += OnTreeHardwareGroupsChanged;
        }


        // === bindable properties ===

        public ObservableCollection<LhmCpuInstanceViewModel> Cpus { get; }


        // === event handlers ===

        private void OnTreeHardwareGroupsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmHardwareInstance instance in e.NewItems)
            {
                if (instance.Kind == HardwareGroupKind.Cpu) AttachToInstance(instance);
            }
        }


        // === private helpers ===

        private void AttachToInstance(LhmHardwareInstance instance)
        {
            var cpu = new LhmCpuInstanceViewModel(instance.HardwareName);
            Cpus.Add(cpu);

            foreach (var entry in instance.Sensors)
            {
                OnSensorDiscovered(cpu, entry);
            }
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(cpu, e);
        }

        private void OnInstanceSensorsChanged(LhmCpuInstanceViewModel cpu, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnSensorDiscovered(cpu, entry);
            }
        }

        private void OnSensorDiscovered(LhmCpuInstanceViewModel cpu, LhmSensorEntry entry)
        {
            if (entry.SensorType != "Load") return;

            if (entry.Name == "CPU Total")
            {
                cpu.TotalLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                PushDataPoint(cpu.TotalLoad, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(cpu.TotalLoad, entry, e);
            }
            else if (entry.Name.StartsWith("CPU Core #"))
            {
                var core = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                cpu.Cores.Add(core);
                PushDataPoint(core, entry);
                entry.PropertyChanged += (s, e) => OnEntryValueChanged(core, entry, e);
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