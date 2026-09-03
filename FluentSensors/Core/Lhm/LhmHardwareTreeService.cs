using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FluentSensors.Common.Sensors;

namespace FluentSensors.Core.Lhm
{
    // central, single subscriber to HardwareMonitorService.HardwareDataUpdated:
    // turns the raw payload into a grouped, live-updating tree (HardwareInstance -> Sensors) that every page/ViewModel reads
    // from instead of each one scanning the payload itself
    // pure discovery + grouping + live values, nothing else; threshold, min/max/avg, sorting, graphs, hide/show all stay
    // page-specific concerns layered on top of this
    public class LhmHardwareTreeService
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;


        // === singleton instance ===

        // lazy on purpose (like PerformanceViewModel): only created the first time a consumer asks for it
        // note: SensorsViewModel is eager at splash screen and depends on this service, so in practice it still ends up
        // running from app start; accepted side effect, not a bug
        private static LhmHardwareTreeService _instance;
        public static LhmHardwareTreeService Instance => _instance ??= new LhmHardwareTreeService();


        // === constructor ===

        private LhmHardwareTreeService()
        {
            HardwareGroups = new ObservableCollection<LhmHardwareInstance>();

            // captures the thread this singleton is first created on; HardwareDataUpdated fires from the background polling
            // thread, so every mutation below must be marshalled back here
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
                        // value set before adding, so any consumer reacting to Sensors.CollectionChanged already sees the
                        // correct first value instead of the default 0
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
