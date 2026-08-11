using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;


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


        // === static info properties ===

        // RAM never has more than one instance in practice (see class doc comment above), so unlike
        // GPU/Storage/Network this needs no HardwareNameMatcher lookup; WinStaticInfoService.Instance.Memory is
        // taken directly
        // read-only, purely computed, WinStaticInfoService never changes after the singletons first access, so nothing
        // to raise OnPropertyChanged for here
        public string MemoryTotalSlotsText => WinStaticInfoService.Instance.Memory.TotalSlots.ToString();
        public IReadOnlyList<WinMemoryModuleInfo> MemoryModules => WinStaticInfoService.Instance.Memory.Modules;


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}