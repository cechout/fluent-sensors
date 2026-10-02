using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance.Lhm
{
    public class LhmGpuInstanceViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // best-effort match against the WMI GPUs (see HardwareNameMatcher); null without a match
        private readonly WinGpuInfo _staticInfo;

        // switchable D3D engine slots in the Extended Engines group, all on D3dEngineOptions
        public const int D3dEngineSlotCount = 4;
        private readonly SensorGraphViewModel[] _d3dEngineSlots = new SensorGraphViewModel[D3dEngineSlotCount];
        private static readonly string[] D3dEngineSlotPropertyNames =
        {
            nameof(D3dEngineSlot1), nameof(D3dEngineSlot2), nameof(D3dEngineSlot3), nameof(D3dEngineSlot4)
        };


        // === constructor ===

        public LhmGpuInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;

            _staticInfo = HardwareNameMatcher.FindBestMatch(
                hardwareName,
                WinStaticInfoService.Instance.Gpus,
                gpu => gpu.Name);

            TemperatureOptions = new ObservableCollection<SensorSwitchCandidate>();
            PackagePowerOptions = new ObservableCollection<SensorSwitchCandidate>();
            ExtendedPackagePowerOptions = new ObservableCollection<SensorSwitchCandidate>();
            MemoryUsedOptions = new ObservableCollection<SensorSwitchCandidate>();
            D3dEngineOptions = new ObservableCollection<SensorSwitchCandidate>();
        }


        // === bindable properties ===

        public string HardwareName { get; }

        // overview categories; the public setter persists the choice, SetXWithoutPersisting is for the default or
        // restored graph (PackagePower and MemoryUsed alike)
        // CoreLoad has no switch partner and stays a plain graph
        private SensorGraphViewModel _coreLoad;
        public SensorGraphViewModel CoreLoad
        {
            get => _coreLoad;
            set { _coreLoad = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _temperature;
        public SensorGraphViewModel Temperature
        {
            get => _temperature;
            set
            {
                if (_temperature == value) return;
                _temperature = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "Temperature", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> TemperatureOptions { get; }

        internal void SetTemperatureWithoutPersisting(SensorGraphViewModel value)
        {
            _temperature = value;
            OnPropertyChanged(nameof(Temperature));
        }

        private SensorGraphViewModel _packagePower;
        public SensorGraphViewModel PackagePower
        {
            get => _packagePower;
            set
            {
                if (_packagePower == value) return;
                _packagePower = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "Power", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> PackagePowerOptions { get; }

        internal void SetPackagePowerWithoutPersisting(SensorGraphViewModel value)
        {
            _packagePower = value;
            OnPropertyChanged(nameof(PackagePower));
        }

        private bool _isShowingExtended;
        public bool IsShowingExtended
        {
            get => _isShowingExtended;
            set
            {
                if (_isShowingExtended != value)
                {
                    _isShowingExtended = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(OverallOpacity));
                    OnPropertyChanged(nameof(OverallIsHitTestVisible));
                    OnPropertyChanged(nameof(ExtendedOpacity));
                    OnPropertyChanged(nameof(ExtendedIsHitTestVisible));
                }
            }
        }

        // --- workaround: SensorGraphControl permanently blank after Collapsed + Unload/Reload ---
        // problem and fix: see GpuDetailView.SetLayoutActive; the Overall/Extended switch hides by
        // Opacity and IsHitTestVisible too
        public double OverallOpacity => IsShowingExtended ? 0 : 1;
        public bool OverallIsHitTestVisible => !IsShowingExtended;

        public double ExtendedOpacity => IsShowingExtended ? 1 : 0;
        public bool ExtendedIsHitTestVisible => IsShowingExtended;

        // Extended Core group; the CoreLoad sensor with a graph of its own (a graph keeps the points of one span, the
        // two views plot different spans)
        private SensorGraphViewModel _extendedCoreLoad;
        public SensorGraphViewModel ExtendedCoreLoad
        {
            get => _extendedCoreLoad;
            set { _extendedCoreLoad = value; OnPropertyChanged(); }
        }

        // Extended Core group; a Power slot with its own candidates and choice, like ExtendedCoreLoad
        private SensorGraphViewModel _extendedPackagePower;
        public SensorGraphViewModel ExtendedPackagePower
        {
            get => _extendedPackagePower;
            set
            {
                if (_extendedPackagePower == value) return;
                _extendedPackagePower = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "ExtendedPower", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> ExtendedPackagePowerOptions { get; }

        internal void SetExtendedPackagePowerWithoutPersisting(SensorGraphViewModel value)
        {
            _extendedPackagePower = value;
            OnPropertyChanged(nameof(ExtendedPackagePower));
        }

        // Extended Core group; a fixed anchor
        private SensorGraphViewModel _coreClock;
        public SensorGraphViewModel CoreClock
        {
            get => _coreClock;
            set { _coreClock = value; OnPropertyChanged(); }
        }

        // Extended Core group; beside HotSpotTemperature, whatever the overview slot shows
        private SensorGraphViewModel _coreTemperature;
        public SensorGraphViewModel CoreTemperature
        {
            get => _coreTemperature;
            set { _coreTemperature = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _hotSpotTemperature;
        public SensorGraphViewModel HotSpotTemperature
        {
            get => _hotSpotTemperature;
            set { _hotSpotTemperature = value; OnPropertyChanged(); }
        }

        // Extended Memory group; the LHM reading, the D3D figure or the free complement
        private SensorGraphViewModel _memoryUsed;
        public SensorGraphViewModel MemoryUsed
        {
            get => _memoryUsed;
            set
            {
                if (_memoryUsed == value) return;
                _memoryUsed = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "MemoryUsed", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> MemoryUsedOptions { get; }

        internal void SetMemoryUsedWithoutPersisting(SensorGraphViewModel value)
        {
            _memoryUsed = value;
            OnPropertyChanged(nameof(MemoryUsed));
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

        private SensorGraphViewModel _d3dSharedMemoryUsed;
        public SensorGraphViewModel D3dSharedMemoryUsed
        {
            get => _d3dSharedMemoryUsed;
            set { _d3dSharedMemoryUsed = value; OnPropertyChanged(); }
        }

        // not charted; the y-axis maximum of MemoryUsed, the reported total
        private double _memoryTotal;
        public double MemoryTotal
        {
            get => _memoryTotal;
            set { _memoryTotal = value; OnPropertyChanged(); }
        }

        // Extended PCIe and Bus group; no alternatives, no Options list
        private SensorGraphViewModel _pcieRx;
        public SensorGraphViewModel PcieRx
        {
            get => _pcieRx;
            set { _pcieRx = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _pcieTx;
        public SensorGraphViewModel PcieTx
        {
            get => _pcieTx;
            set { _pcieTx = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _busLoad;
        public SensorGraphViewModel BusLoad
        {
            get => _busLoad;
            set { _busLoad = value; OnPropertyChanged(); }
        }

        // Extended Engines group; VideoEngineLoad is a fixed anchor, D3dEngineSlot1..4 switch
        // over the D3dEngineOptions pool
        private SensorGraphViewModel _videoEngineLoad;
        public SensorGraphViewModel VideoEngineLoad
        {
            get => _videoEngineLoad;
            set { _videoEngineLoad = value; OnPropertyChanged(); }
        }

        public ObservableCollection<SensorSwitchCandidate> D3dEngineOptions { get; }

        public SensorGraphViewModel D3dEngineSlot1 { get => _d3dEngineSlots[0]; set => SetD3dEngineSlot(0, value); }
        public SensorGraphViewModel D3dEngineSlot2 { get => _d3dEngineSlots[1]; set => SetD3dEngineSlot(1, value); }
        public SensorGraphViewModel D3dEngineSlot3 { get => _d3dEngineSlots[2]; set => SetD3dEngineSlot(2, value); }
        public SensorGraphViewModel D3dEngineSlot4 { get => _d3dEngineSlots[3]; set => SetD3dEngineSlot(3, value); }

        // the persistence key of a slot, shared with LhmGpuPerformanceViewModel
        public static string D3dEngineCategory(int slotIndex) => $"D3DEngine{slotIndex + 1}";

        internal SensorGraphViewModel GetD3dEngineSlot(int slotIndex) => _d3dEngineSlots[slotIndex];

        internal void SetD3dEngineSlotWithoutPersisting(int slotIndex, SensorGraphViewModel value)
        {
            _d3dEngineSlots[slotIndex] = value;
            OnPropertyChanged(D3dEngineSlotPropertyNames[slotIndex]);
        }


        // === static info text properties ===
        // computed from the matched WinGpuInfo
        public string GpuNameText => _staticInfo?.Name ?? "-";
        public string GpuVendorText => _staticInfo != null ? HardwareInfoFormatter.FormatVendorName(_staticInfo.VendorId) : "-";
        public string GpuDriverVersionText => _staticInfo?.DriverVersion ?? "-";
        public string GpuDedicatedMemoryText => _staticInfo != null ? HardwareInfoFormatter.FormatBytesAsGb(_staticInfo.DedicatedVideoMemoryBytes) : "-";
        public string GpuSharedMemoryText => _staticInfo != null ? HardwareInfoFormatter.FormatBytesAsGb(_staticInfo.SharedSystemMemoryBytes) : "-";
        public string GpuDeviceIdText => _staticInfo != null ? HardwareInfoFormatter.FormatPciId(_staticInfo.DeviceId) : "-";
        public string GpuPnpDeviceIdText => _staticInfo?.PnpDeviceId ?? "-";


        // === private helpers ===

        // the D3dEngineSlot setters; persisted per slot ("D3DEngine1" to "D3DEngine4")
        private void SetD3dEngineSlot(int slotIndex, SensorGraphViewModel value)
        {
            if (_d3dEngineSlots[slotIndex] == value) return;
            _d3dEngineSlots[slotIndex] = value;
            OnPropertyChanged(D3dEngineSlotPropertyNames[slotIndex]);
            if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, D3dEngineCategory(slotIndex), value.SensorId);
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
