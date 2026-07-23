using Microsoft.UI.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using FluentSensors.Common;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core;


namespace FluentSensors.Features.Performance.Lhm
{
    public class LhmCpuPerformanceViewModel : INotifyPropertyChanged
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;
        private const int TotalLoadDataPoints = 110;
        private const int CoreDataPoints = 50;



        // === constructor ===

        public LhmCpuPerformanceViewModel()
        {
            Cores = new ObservableCollection<SensorGraphViewModel>();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            HardwareMonitorService.Instance.HardwareDataUpdated += OnHardwareDataUpdated;
        }


        // === bindable properties ===

        // overall CPU load; created lazily once the first "CPU Total" sample arrives
        private SensorGraphViewModel _totalLoad;
        public SensorGraphViewModel TotalLoad
        {
            get => _totalLoad;
            private set { _totalLoad = value; OnPropertyChanged(); }
        }

        // one graph per "CPU Core #N[ Thread #M]" sensor
        public ObservableCollection<SensorGraphViewModel> Cores { get; }

        // controls which of the two views is currently shown
        // (single overall graph or grid of all cores/threads)
        private bool _isShowingAllThreads;
        public bool IsShowingAllThreads
        {
            get => _isShowingAllThreads;
            set
            {
                if (_isShowingAllThreads != value)
                {
                    _isShowingAllThreads = value;
                    OnPropertyChanged();
                }
            }
        }


        // === event handlers ===

        private void OnHardwareDataUpdated(List<SensorData> payload)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                int cpuLoadCount = payload.Count(d => d.HardwareType == "Cpu" && d.SensorType == "Load");

                foreach (var data in payload)
                {
                    if (data.HardwareType != "Cpu" || data.SensorType != "Load") continue;

                    if (data.Name == "CPU Total")
                    {
                        if (TotalLoad == null)
                        {
                            TotalLoad = new SensorGraphViewModel(data.Id, data.Name, data.SensorType, TotalLoadDataPoints);
                        }
                        TotalLoad.AddDataPoint(data.Value, SensorUnitFormatter.Format(data.Value, data.SensorType));
                    }
                    else if (data.Name.StartsWith("CPU Core #"))
                    {
                        var core = Cores.FirstOrDefault(c => c.SensorId == data.Id);

                        if (core == null)
                        {
                            core = new SensorGraphViewModel(data.Id, data.Name, data.SensorType, CoreDataPoints);
                            Cores.Add(core);
                        }

                        core.AddDataPoint(data.Value, SensorUnitFormatter.Format(data.Value, data.SensorType));
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