using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance.Lhm
{
    // one memory group:
    // in practice the single "Total Memory" of LHM (more would be NUMA), with the separate "Virtual Memory" group
    // and the module groups (one per DIMM that answers on the bus) folded in, one RAM view; a data holder,
    // LhmMemoryPerformanceViewModel parses
    public class LhmMemoryInstanceViewModel : INotifyPropertyChanged
    {
        // === constructor ===

        public LhmMemoryInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;

            ModuleTemperatureOptions = new ObservableCollection<SensorSwitchCandidate>();
            MemoryModules = WinStaticInfoService.Instance.Memory.Modules
                .Select(module => new LhmMemoryModuleViewModel(module))
                .ToList();
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

        // Used + Available rounded up to 4 GB, the Used graph y-max; a readable total instead of "31.7"
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

        // Used + Available of virtual memory, unrounded; the graph y-max
        private double _virtualMemoryTotal;
        public double VirtualMemoryTotal
        {
            get => _virtualMemoryTotal;
            set { _virtualMemoryTotal = value; OnPropertyChanged(); }
        }


        // the temperature of one module; a switch once several report one
        // persisted under a fixed name, HardwareName depends on which LHM group came first
        private SensorGraphViewModel _moduleTemperature;
        public SensorGraphViewModel ModuleTemperature
        {
            get => _moduleTemperature;
            set
            {
                if (_moduleTemperature == value) return;
                _moduleTemperature = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(SwitchStateName, "ModuleTemperature", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> ModuleTemperatureOptions { get; }

        internal const string SwitchStateName = "Total Memory";

        internal void SetModuleTemperatureWithoutPersisting(SensorGraphViewModel value)
        {
            _moduleTemperature = value;
            OnPropertyChanged(nameof(ModuleTemperature));
            OnPropertyChanged(nameof(HasModuleTemperature));
        }

        // the third graph and its tile only take room once a module reports a temperature; many do not (DDR4 without a
        // sensor, soldered memory, no PawnIO)
        public bool HasModuleTemperature => _moduleTemperature != null;


        // === static info properties ===

        // static info; one RAM instance, so WinStaticInfoService.Instance.Memory directly without
        // HardwareNameMatcher, and it never changes
        public string MemoryTotalSlotsText => WinStaticInfoService.Instance.Memory.TotalSlots.ToString();
        public string MemoryMaxCapacityText => WinStaticInfoService.Instance.Memory.MaxCapacityBytes > 0
            ? HardwareInfoFormatter.FormatBytesAsGb(WinStaticInfoService.Instance.Memory.MaxCapacityBytes)
            : "-";
        public string MemoryErrorCorrectionText => HardwareInfoFormatter.FormatErrorCorrection(WinStaticInfoService.Instance.Memory.ErrorCorrection);

        // one card per module Windows lists; the SPD facts of each are filled by LhmMemoryPerformanceViewModel
        public IReadOnlyList<LhmMemoryModuleViewModel> MemoryModules { get; }

        // the performance page name, RoundedTotalMemory and the SmbiosMemoryType of the first module; HardwareName
        // until both exist (and the raw LHM name everywhere else)
        public string PerformanceDisplayName
        {
            get
            {
                if (RoundedTotalMemory <= 0) return HardwareName;

                string sizeText = $"{RoundedTotalMemory:0} GB";
                var modules = MemoryModules;
                if (modules == null || modules.Count == 0) return sizeText;

                string typeText = HardwareInfoFormatter.FormatMemoryType(modules[0].Info.SmbiosMemoryType);
                return $"{sizeText} {typeText}";
            }
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
