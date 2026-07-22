using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using FluentSensors.Common;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core;


namespace FluentSensors.Features.Performance.Lhm
{
    public class LhmNetworkPerformanceViewModel
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;


        // === constructor ===

        public LhmNetworkPerformanceViewModel()
        {
            Adapters = new ObservableCollection<LhmNetworkInstanceViewModel>();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;
        }


        // === bindable properties ===

        public ObservableCollection<LhmNetworkInstanceViewModel> Adapters { get; }


        // === event handlers ===

        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var data in payload)
                {
                    if (data.HardwareType != "Network") continue;
                    if (data.Name != "Upload Speed" && data.Name != "Download Speed") continue;

                    var adapter = Adapters.FirstOrDefault(a => a.HardwareName == data.HardwareName);
                    if (adapter == null)
                    {
                        adapter = new LhmNetworkInstanceViewModel(data.HardwareName);
                        Adapters.Add(adapter);
                    }

                    string formatted = SensorUnitFormatter.Format(data.Value, data.SensorType);

                    if (data.Name == "Upload Speed")
                    {
                        if (adapter.UploadSpeed == null) adapter.UploadSpeed = new SensorGraphViewModel(data.Id, data.Name, data.SensorType);
                        adapter.UploadSpeed.AddDataPoint(data.Value, formatted);
                    }
                    else
                    {
                        if (adapter.DownloadSpeed == null) adapter.DownloadSpeed = new SensorGraphViewModel(data.Id, data.Name, data.SensorType);
                        adapter.DownloadSpeed.AddDataPoint(data.Value, formatted);
                    }
                }
            });
        }
    }
}