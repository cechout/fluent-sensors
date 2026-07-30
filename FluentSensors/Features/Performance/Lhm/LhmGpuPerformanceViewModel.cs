using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.Lhm;


namespace FluentSensors.Features.Performance.Lhm
{
    // discovers every GPU instance (dGPU + iGPU both count separately) from LhmHardwareTreeService and creates
    // one LhmGpuInstanceViewModel per instance; parses each raw LHM sensor into the right property on the right
    // instance
    // the instance itself stays a dumb data holder
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

        // matches on (Name, SensorType) rather than Name alone: "GPU Core" is reported both as a Load sensor
        // (utilization %) and a Clock sensor (MHz) with the exact same name, so Name-only matching would let one
        // silently overwrite the other
        private void OnSensorDiscovered(LhmGpuInstanceViewModel gpu, LhmSensorEntry entry)
        {
            switch (entry.Name, entry.SensorType)
            {
                case ("GPU Core", "Load"):
                    gpu.CoreLoad = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.CoreLoad, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.CoreLoad, entry, e);
                    break;

                case ("GPU Core", "Clock"):
                    gpu.CoreClock = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.CoreClock, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.CoreClock, entry, e);
                    break;

                case ("GPU Hot Spot", "Temperature"):
                    gpu.HotSpotTemperature = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.HotSpotTemperature, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.HotSpotTemperature, entry, e);
                    break;

                case ("GPU Package", "Power"):
                    gpu.PackagePower = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.PackagePower, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.PackagePower, entry, e);
                    break;

                case ("GPU Memory Used", "SmallData"):
                    gpu.MemoryUsed = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.MemoryUsed, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.MemoryUsed, entry, e);
                    break;

                case ("GPU Memory Total", "SmallData"):
                    gpu.MemoryTotal = entry.Value;
                    entry.PropertyChanged += (s, e) => OnMemoryTotalChanged(gpu, entry, e);
                    break;

                case ("GPU Memory", "Clock"):
                    gpu.MemoryClock = new SensorGraphViewModel(entry.Id, entry.Name, entry.SensorType);
                    PushDataPoint(gpu.MemoryClock, entry);
                    entry.PropertyChanged += (s, e) => OnEntryValueChanged(gpu.MemoryClock, entry, e);
                    break;

                case ("GPU Memory Controller", "Load"):
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

        private static void OnMemoryTotalChanged(LhmGpuInstanceViewModel gpu, LhmSensorEntry entry, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LhmSensorEntry.Value)) return;
            gpu.MemoryTotal = entry.Value;
        }

        private static void PushDataPoint(SensorGraphViewModel graph, LhmSensorEntry entry)
        {
            graph.AddDataPoint(entry.Value, SensorUnitFormatter.Format(entry.Value, entry.SensorType));
        }
    }
}