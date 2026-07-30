using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Features.Performance.Lhm
{
    // one entry per detected physical memory group; LHM reports this as a single "Total Memory" instance in
    // practice, but this stays correct if that ever differs on unusual hardware (e.g. NUMA)
    // Also absorbs LHMs separate "Virtual Memory" hardware group into this same instance, since the app shows both as
    // one combined RAM view rather than a separate nav entry
    // A plain data holder; all sensor discovery/parsing lives in LhmMemoryPerformanceViewModel instead
    public class LhmMemoryInstanceViewModel : INotifyPropertyChanged
    {
        // === constructor ===

        public LhmMemoryInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;
        }


        // === bindable properties ===

        public string HardwareName { get; }

        private SensorGraphViewModel _used;
        public SensorGraphViewModel Used
        {
            get => _used;
            set { _used = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _available;
        public SensorGraphViewModel Available
        {
            get => _available;
            set { _available = value; OnPropertyChanged(); }
        }

        // Used + Available, rounded up to a clean step (see LhmMemoryPerformanceViewModel); used as the Y-max for
        // the Used graph so the axis shows a readable total instead of e.g. "31.7"
        private double _roundedTotalMemory;
        public double RoundedTotalMemory
        {
            get => _roundedTotalMemory;
            set { _roundedTotalMemory = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _virtualMemoryUsed;
        public SensorGraphViewModel VirtualMemoryUsed
        {
            get => _virtualMemoryUsed;
            set { _virtualMemoryUsed = value; OnPropertyChanged(); }
        }

        // Used + Available for virtual memory, deliberately NOT rounded (unlike RoundedTotalMemory); used purely
        // as this graphs own Y-max
        private double _virtualMemoryTotal;
        public double VirtualMemoryTotal
        {
            get => _virtualMemoryTotal;
            set { _virtualMemoryTotal = value; OnPropertyChanged(); }
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}