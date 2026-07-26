using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core;


namespace FluentSensors.Features.Performance.Lhm
{
    public class LhmGpuPerformanceViewModel
    {
        // === constructor ===

        public LhmGpuPerformanceViewModel()
        {
            Gpus = new ObservableCollection<LhmGpuInstanceViewModel>();

            var tree = LhmHardwareTreeService.Instance;

            foreach (var instance in tree.HardwareGroups)
            {
                if (instance.Kind == HardwareGroupKind.Gpu) AttachToInstance(instance);
            }
            tree.HardwareGroups.CollectionChanged += OnTreeHardwareGroupsChanged;
        }


        // === bindable properties ===

        public ObservableCollection<LhmGpuInstanceViewModel> Gpus { get; }


        // === event handlers ===

        private void OnTreeHardwareGroupsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmHardwareInstance instance in e.NewItems)
            {
                if (instance.Kind == HardwareGroupKind.Gpu) AttachToInstance(instance);
            }
        }


        // === private helpers ===

        private void AttachToInstance(LhmHardwareInstance instance)
        {
            var gpu = new LhmGpuInstanceViewModel(instance.HardwareName);
            Gpus.Add(gpu);

            foreach (var entry in instance.Sensors)
            {
                OnSensorDiscovered(gpu, entry);
            }
            instance.Sensors.CollectionChanged += (s, e) => OnInstanceSensorsChanged(gpu, e);
        }

        private void OnInstanceSensorsChanged(LhmGpuInstanceViewModel gpu, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            foreach (LhmSensorEntry entry in e.NewItems)
            {
                OnSensorDiscovered(gpu, entry);
            }
        }

        private void OnSensorDiscovered(LhmGpuInstanceViewModel gpu, LhmSensorEntry entry)
        {
            switch (entry.Name)
            {
                case "GPU Core":
                    gpu.CoreLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.CoreLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.CoreLoad, entry, e);
                    break;

                case "GPU Memory Used":
                    gpu.MemoryUsed = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.MemoryUsed, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.MemoryUsed, entry, e);
                    break;

                case "GPU Memory Controller":
                    gpu.MemoryControllerLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.MemoryControllerLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.MemoryControllerLoad, entry, e);
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