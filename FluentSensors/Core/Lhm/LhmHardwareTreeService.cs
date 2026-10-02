using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FluentSensors.Common.Sensors;

namespace FluentSensors.Core.Lhm
{
    // the hardware tree:
    // the one HardwareDataUpdated subscriber, a live tree (hardware instance, its sensors) that every page
    // reads instead of the payload
    // discovery, grouping and live values only; thresholds, statistics, sorting, graphs and hiding belong to the pages
    public class LhmHardwareTreeService
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;


        // === singleton instance ===

        // lazy like PerformanceViewModel; the eager SensorsViewModel creates it at the splash anyway
        private static LhmHardwareTreeService _instance;
        public static LhmHardwareTreeService Instance => _instance ??= new LhmHardwareTreeService();


        // === constructor ===

        private LhmHardwareTreeService()
        {
            HardwareGroups = new ObservableCollection<LhmHardwareInstance>();

            // the creating thread; HardwareDataUpdated fires on the polling thread, so every mutation comes back here
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;
        }


        // === bindable properties ===

        public ObservableCollection<LhmHardwareInstance> HardwareGroups { get; }


        // === event handlers ===

        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var data in payload)
                {
                    var instance = HardwareGroups.FirstOrDefault(g => g.HardwareName == data.HardwareName);
                    if (instance == null)
                    {
                        instance = new LhmHardwareInstance(data.HardwareName, HardwareGroupInfo.GetKind(data.HardwareType));
                        HardwareGroups.Add(instance);
                    }

                    var entry = instance.Sensors.FirstOrDefault(s => s.Id == data.Id);
                    if (entry == null)
                    {
                        // the value before the add, so a CollectionChanged consumer never sees the default 0
                        entry = new LhmSensorEntry(data.Id, data.Name, data.SensorType);
                        entry.Value = data.Value;
                        instance.Sensors.Add(entry);
                    }
                    else
                    {
                        entry.Value = data.Value;
                    }
                }
            });
        }
    }
}
