using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Common.Localization;
using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance.Lhm
{
    public class LhmGpuInstanceViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // best-effort match against the WMI GPUs (see HardwareNameMatcher); null without a match
        private WinGpuInfo _staticInfo;

        // switchable D3D engine slots in the Extended Engines group, all on D3dEngineOptions
        public const int D3dEngineSlotCount = 4;
        private readonly SensorGraphViewModel[] _d3dEngineSlots = new SensorGraphViewModel[D3dEngineSlotCount];
        private static readonly string[] D3dEngineSlotPropertyNames =
        {
            nameof(D3dEngineSlot1), nameof(D3dEngineSlot2), nameof(D3dEngineSlot3), nameof(D3dEngineSlot4)
        };


        // === constructor ===

        public LhmGpuInstanceViewModel(string hardwareName, HardwareGroupKind kind)
        {
            HardwareName = hardwareName;
            Kind = kind;

            _staticInfo = MatchStaticInfo();

            // never detached; an instance lives for the session
            WinStaticInfoService.Instance.GpusChanged += OnStaticInfoChanged;

            CoreLoadOptions = new ObservableCollection<SensorSwitchCandidate>();
            TemperatureOptions = new ObservableCollection<SensorSwitchCandidate>();
            ExtendedTemperatureOptions = new ObservableCollection<SensorSwitchCandidate>();
            SharedMemoryOptions = new ObservableCollection<SensorSwitchCandidate>();
            PackagePowerOptions = new ObservableCollection<SensorSwitchCandidate>();
            ExtendedPackagePowerOptions = new ObservableCollection<SensorSwitchCandidate>();
            MemoryUsedOptions = new ObservableCollection<SensorSwitchCandidate>();
            D3dEngineOptions = new ObservableCollection<SensorSwitchCandidate>();
            OtherSensors = new ObservableCollection<SensorGraphViewModel>();
        }


        // === bindable properties ===

        public string HardwareName { get; }

        // DiscreteGpu or IntegratedGpu; the Extended view drops the groups an integrated gpu has no sensors for
        public HardwareGroupKind Kind { get; }
        public bool IsDiscrete => Kind != HardwareGroupKind.IntegratedGpu;
        public bool IsIntegrated => !IsDiscrete;

        // overview categories; the public setter persists the choice, SetXWithoutPersisting is for the default or
        // restored graph (PackagePower and MemoryUsed alike)
        // CoreLoad switches between the core load and the D3D engines, an Intel integrated gpu has only the latter
        private SensorGraphViewModel _coreLoad;
        public SensorGraphViewModel CoreLoad
        {
            get => _coreLoad;
            set
            {
                if (_coreLoad == value) return;
                _coreLoad = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "Load", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> CoreLoadOptions { get; }

        internal void SetCoreLoadWithoutPersisting(SensorGraphViewModel value)
        {
            _coreLoad = value;
            OnPropertyChanged(nameof(CoreLoad));
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

        // Extended Core group; a Temperature slot with its own candidates and choice, like ExtendedPackagePower
        private SensorGraphViewModel _extendedTemperature;
        public SensorGraphViewModel ExtendedTemperature
        {
            get => _extendedTemperature;
            set
            {
                if (_extendedTemperature == value) return;
                _extendedTemperature = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "ExtendedTemperature", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> ExtendedTemperatureOptions { get; }

        internal void SetExtendedTemperatureWithoutPersisting(SensorGraphViewModel value)
        {
            _extendedTemperature = value;
            OnPropertyChanged(nameof(ExtendedTemperature));
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

        // how full the vram is, in percent; (discrete only)
        private SensorGraphViewModel _memoryLoad;
        public SensorGraphViewModel MemoryLoad
        {
            get => _memoryLoad;
            set { _memoryLoad = value; OnPropertyChanged(); }
        }

        // Extended Memory group; system RAM lent to the gpu, used or its free complement
        private SensorGraphViewModel _sharedMemory;
        public SensorGraphViewModel SharedMemory
        {
            get => _sharedMemory;
            set
            {
                if (_sharedMemory == value) return;
                _sharedMemory = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "SharedMemory", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> SharedMemoryOptions { get; }

        internal void SetSharedMemoryWithoutPersisting(SensorGraphViewModel value)
        {
            _sharedMemory = value;
            OnPropertyChanged(nameof(SharedMemory));
        }

        // not charted; the y-axis maximum of SharedMemory, in the unit of its sensors
        // the D3D total where LHM reports one (Intel, AMD), else what DXGI lists for the adapter (NVIDIA)
        public double SharedMemoryTotal
        {
            get
            {
                if (_reportedSharedMemoryTotal > 0) return _reportedSharedMemoryTotal;
                // NaN leaves the panel its own maximum
                if (_staticInfo == null || _staticInfo.SharedSystemMemoryBytes == 0) return double.NaN;

                double megabytes = _staticInfo.SharedSystemMemoryBytes / 1_048_576.0;
                return IsSharedMemoryInGigabytes ? megabytes / 1024.0 : megabytes;
            }
        }

        private double _reportedSharedMemoryTotal;
        internal double ReportedSharedMemoryTotal
        {
            get => _reportedSharedMemoryTotal;
            set { _reportedSharedMemoryTotal = value; OnPropertyChanged(nameof(SharedMemoryTotal)); }
        }

        // LHM reports gpu memory in MB up to 0.9.6 and in GB after it
        private bool _isSharedMemoryInGigabytes;
        internal bool IsSharedMemoryInGigabytes
        {
            get => _isSharedMemoryInGigabytes;
            set { _isSharedMemoryInGigabytes = value; OnPropertyChanged(nameof(SharedMemoryTotal)); }
        }

        // not charted; the y-axis maximum of MemoryUsed, the reported total (the D3D total where the driver has none)
        internal bool HasDriverMemoryTotal { get; set; } // "GPU Memory Total" exists and wins
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

        // Extended Other group; every sensor without a place above, whatever the vendor or the driver calls it
        public ObservableCollection<SensorGraphViewModel> OtherSensors { get; }
        public bool HasOtherSensors => OtherSensors.Count > 0;

        internal void AddOtherSensor(SensorGraphViewModel graph)
        {
            OtherSensors.Add(graph);
            if (OtherSensors.Count == 1) OnPropertyChanged(nameof(HasOtherSensors));
        }

        // the display names of the D3D nodes by their sensor name, each a setter; a name that occurs more than
        // once gets a number ("D3D Copy [2]"), the first one too as soon as a second arrives
        private readonly Dictionary<string, List<Action<string>>> _d3dDisplayNameSetters = new();

        internal void TrackD3dDisplayName(string sensorName, Action<string> setDisplayName)
        {
            if (!_d3dDisplayNameSetters.TryGetValue(sensorName, out var setters))
            {
                setters = new List<Action<string>>();
                _d3dDisplayNameSetters[sensorName] = setters;
            }
            setters.Add(setDisplayName);

            if (setters.Count < 2) return;

            // the Intel driver names some nodes "D3D " with a trailing space
            string baseName = sensorName.Trim();
            int number = setters.Count;
            if (number == 2) setters[0]($"{baseName} [1]");
            setDisplayName($"{baseName} [{number}]");
        }

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
        public string GpuDriverDateText => HardwareInfoFormatter.FormatTextOrDash(_staticInfo?.DriverDate);
        public string GpuTypeText => AppTerms.Get(IsIntegrated ? "Gpu_TypeIntegrated" : "Gpu_TypeDiscrete");
        public string GpuBoardVendorText => _staticInfo != null ? HardwareInfoFormatter.FormatBoardVendor(_staticInfo.PnpDeviceId) : "-";
        public string GpuDisplayText => _staticInfo != null && HardwareInfoFormatter.FormatGpuDisplay(_staticInfo) is { Length: > 0 } display ? display : "-";
        public string GpuDedicatedMemoryText => _staticInfo != null ? HardwareInfoFormatter.FormatBytesAsGb(_staticInfo.DedicatedVideoMemoryBytes) : "-";
        public string GpuSharedMemoryText => _staticInfo != null ? HardwareInfoFormatter.FormatBytesAsGb(_staticInfo.SharedSystemMemoryBytes) : "-";
        public string GpuDeviceIdText => _staticInfo != null ? HardwareInfoFormatter.FormatPciId(_staticInfo.DeviceId) : "-";
        public string GpuPnpDeviceIdText => _staticInfo?.PnpDeviceId ?? "-";

        // the PCI Express link; the section only shows for a gpu that has one
        public bool HasPciLink => _staticInfo?.PciLink != null;
        public string GpuPciLinkText => HardwareInfoFormatter.FormatPciLinkPair(_staticInfo?.PciLink);


        // === event handlers ===

        // Windows was asked again (a driver update, a monitor moved to another gpu); every static text re-reads
        private void OnStaticInfoChanged()
        {
            _staticInfo = MatchStaticInfo();

            foreach (string propertyName in StaticInfoPropertyNames) OnPropertyChanged(propertyName);
        }

        private static readonly string[] StaticInfoPropertyNames =
        {
            nameof(GpuNameText), nameof(GpuVendorText), nameof(GpuDriverVersionText),
            nameof(GpuDisplayText), nameof(GpuDedicatedMemoryText), nameof(GpuSharedMemoryText),
            nameof(GpuDeviceIdText), nameof(GpuPnpDeviceIdText), nameof(SharedMemoryTotal),
            nameof(GpuDriverDateText), nameof(GpuBoardVendorText), nameof(HasPciLink),
            nameof(GpuPciLinkText)
        };


        // === private helpers ===

        private WinGpuInfo MatchStaticInfo() => HardwareNameMatcher.FindBestMatch(
            HardwareName,
            WinStaticInfoService.Instance.Gpus,
            gpu => gpu.Name);

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
