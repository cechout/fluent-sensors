using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Features.Performance.Lhm
{
    // one entry per detected GPU (a laptop with dGPU + iGPU shows two):
    // exposes whichever of the three tracked metrics that specific GPU actually reports; Intel iGPUs, for example, do not
    // expose "GPU Core" or "GPU Memory Controller", so those two stay null for that instance
    public class LhmGpuInstanceViewModel : INotifyPropertyChanged
    {
        public string HardwareName { get; }

        public LhmGpuInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;
        }

        private SensorGraphViewModel _coreLoad;
        public SensorGraphViewModel CoreLoad
        {
            get => _coreLoad;
            set { _coreLoad = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _memoryUsed;
        public SensorGraphViewModel MemoryUsed
        {
            get => _memoryUsed;
            set { _memoryUsed = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _memoryControllerLoad;
        public SensorGraphViewModel MemoryControllerLoad
        {
            get => _memoryControllerLoad;
            set { _memoryControllerLoad = value; OnPropertyChanged(); }
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}