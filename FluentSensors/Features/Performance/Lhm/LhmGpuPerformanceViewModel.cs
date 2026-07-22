using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using FluentSensors.Common;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core;


namespace FluentSensors.Features.Performance.Lhm
{
    public class LhmGpuPerformanceViewModel
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;


        // === constructor ===

        public LhmGpuPerformanceViewModel()
        {
            Gpus = new ObservableCollection<LhmGpuInstanceViewModel>();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;
        }


        // === bindable properties ===

        public ObservableCollection<LhmGpuInstanceViewModel> Gpus { get; }


        // === event handlers ===

        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var data in payload)
                {
                    if (!IsGpuHardware(data.HardwareType)) continue;
                    if (data.Name != "GPU Core" && data.Name != "GPU Memory Used" && data.Name != "GPU Memory Controller") continue;

                    var gpu = Gpus.FirstOrDefault(g => g.HardwareName == data.HardwareName);
                    if (gpu == null)
                    {
                        gpu = new LhmGpuInstanceViewModel(data.HardwareName);
                        Gpus.Add(gpu);
                    }

                    string formatted = SensorUnitFormatter.Format(data.Value, data.SensorType);

                    switch (data.Name)
                    {
                        case "GPU Core":
                            if (gpu.CoreLoad == null) gpu.CoreLoad = new SensorGraphViewModel(data.Id, data.Name, data.SensorType);
                            gpu.CoreLoad.AddDataPoint(data.Value, formatted);
                            break;

                        case "GPU Memory Used":
                            if (gpu.MemoryUsed == null) gpu.MemoryUsed = new SensorGraphViewModel(data.Id, data.Name, data.SensorType);
                            gpu.MemoryUsed.AddDataPoint(data.Value, formatted);
                            break;

                        case "GPU Memory Controller":
                            if (gpu.MemoryControllerLoad == null) gpu.MemoryControllerLoad = new SensorGraphViewModel(data.Id, data.Name, data.SensorType);
                            gpu.MemoryControllerLoad.AddDataPoint(data.Value, formatted);
                            break;
                    }
                }
            });
        }


        // === private helpers ===

        private static bool IsGpuHardware(string hardwareType)
        {
            return hardwareType == "GpuNvidia" || hardwareType == "GpuAmd" || hardwareType == "GpuIntel";
        }
    }
}