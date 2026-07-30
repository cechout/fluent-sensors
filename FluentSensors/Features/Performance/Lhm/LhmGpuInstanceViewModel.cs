using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Features.Performance.Lhm
{
    // one entry per detected GPU (a laptop with dGPU + iGPU shows two); a plain data holder for whichever of
    // these sensors that specific GPU actually reports
    // An iGPU may leave most of these null since it lacks the corresponding LHM sensors most of the time
    // All sensor discovery/parsing lives in LhmGpuPerformanceViewModel instead
    public class LhmGpuInstanceViewModel : INotifyPropertyChanged
    {
        // === constructor ===

        public LhmGpuInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;
        }


        // === bindable properties ===

        public string HardwareName { get; }

        private SensorGraphViewModel _coreLoad;
        public SensorGraphViewModel CoreLoad
        {
            get => _coreLoad;
            set { _coreLoad = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _hotSpotTemperature;
        public SensorGraphViewModel HotSpotTemperature
        {
            get => _hotSpotTemperature;
            set { _hotSpotTemperature = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _packagePower;
        public SensorGraphViewModel PackagePower
        {
            get => _packagePower;
            set { _packagePower = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _memoryUsed;
        public SensorGraphViewModel MemoryUsed
        {
            get => _memoryUsed;
            set { _memoryUsed = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _coreClock;
        public SensorGraphViewModel CoreClock
        {
            get => _coreClock;
            set { _coreClock = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _memoryClock;
        public SensorGraphViewModel MemoryClock
        {
            get => _memoryClock;
            set { _memoryClock = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _memoryControllerLoad;
        public SensorGraphViewModel MemoryControllerLoad
        {
            get => _memoryControllerLoad;
            set { _memoryControllerLoad = value; OnPropertyChanged(); }
        }

        // not charted, just a Y-max helper for MemoryUsed
        // the hardwares own reported total, no rounding needed
        private double _memoryTotal;
        public double MemoryTotal
        {
            get => _memoryTotal;
            set { _memoryTotal = value; OnPropertyChanged(); }
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}