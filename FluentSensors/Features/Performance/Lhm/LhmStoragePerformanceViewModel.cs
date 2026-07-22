using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using FluentSensors.Common;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core;


namespace FluentSensors.Features.Performance.Lhm
{
    public class LhmStoragePerformanceViewModel
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;


        // === constructor ===

        public LhmStoragePerformanceViewModel()
        {
            Drives = new ObservableCollection<LhmStorageInstanceViewModel>();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;
        }


        // === bindable properties ===

        public ObservableCollection<LhmStorageInstanceViewModel> Drives { get; }


        // === event handlers ===

        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var data in payload)
                {
                    if (data.HardwareType != "Storage") continue;
                    if (data.Name != "Write Rate" && data.Name != "Read Rate") continue;

                    var drive = Drives.FirstOrDefault(d => d.HardwareName == data.HardwareName);
                    if (drive == null)
                    {
                        drive = new LhmStorageInstanceViewModel(data.HardwareName);
                        Drives.Add(drive);
                    }

                    string formatted = SensorUnitFormatter.Format(data.Value, data.SensorType);

                    if (data.Name == "Write Rate")
                    {
                        if (drive.WriteRate == null) drive.WriteRate = new SensorGraphViewModel(data.Id, data.Name, data.SensorType);
                        drive.WriteRate.AddDataPoint(data.Value, formatted);
                    }
                    else
                    {
                        if (drive.ReadRate == null) drive.ReadRate = new SensorGraphViewModel(data.Id, data.Name, data.SensorType);
                        drive.ReadRate.AddDataPoint(data.Value, formatted);
                    }
                }
            });
        }
    }
}