using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Common;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core;


namespace FluentSensors.Features.Performance.Lhm
{
    public class LhmMemoryPerformanceViewModel : INotifyPropertyChanged
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;


        // === constructor ===

        public LhmMemoryPerformanceViewModel()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;
        }


        // === bindable properties ===

        // LHM's raw hardware name for this group (currently always "Total Memory"); captured once, used by
        // PerformanceViewModel to populate the RAM nav item's DisplayName
        private string _hardwareName;
        public string HardwareName
        {
            get => _hardwareName;
            private set { _hardwareName = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _used;
        public SensorGraphViewModel Used
        {
            get => _used;
            private set { _used = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _available;
        public SensorGraphViewModel Available
        {
            get => _available;
            private set { _available = value; OnPropertyChanged(); }
        }


        // === event handlers ===

        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var data in payload)
                {
                    // "Virtual Memory" is the commit charge (incl. page file), we only want the physical RAM group
                    if (data.HardwareType != "Memory" || data.HardwareName != "Total Memory") continue;

                    if (HardwareName == null)
                    {
                        HardwareName = data.HardwareName;
                    }

                    string formatted = SensorUnitFormatter.Format(data.Value, data.SensorType);

                    if (data.Name == "Memory Used")
                    {
                        if (Used == null) Used = new SensorGraphViewModel(data.Id, data.Name, data.SensorType);
                        Used.AddDataPoint(data.Value, formatted);
                    }
                    else if (data.Name == "Memory Available")
                    {
                        if (Available == null) Available = new SensorGraphViewModel(data.Id, data.Name, data.SensorType);
                        Available.AddDataPoint(data.Value, formatted);
                    }
                }
            });
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}