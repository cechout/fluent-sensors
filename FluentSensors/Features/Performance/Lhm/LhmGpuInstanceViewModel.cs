using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;


namespace FluentSensors.Features.Performance.Lhm
{
    public class LhmGpuInstanceViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // best-effort match against WMI-reported GPUs; see HardwareNameMatcher for the matching approach and its
        // limitations; null if no candidate matched at all (e.g. Win32_VideoController returned nothing)
        private readonly WinGpuInfo _staticInfo;


        // === constructor ===

        public LhmGpuInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;

            _staticInfo = HardwareNameMatcher.FindBestMatch(
                hardwareName,
                WinStaticInfoService.Instance.Gpus,
                gpu => gpu.Name);
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


        // === static info text properties ===
        // read-only, purely computed from the matched WinGpuInfo
        public string GpuNameText => _staticInfo?.Name ?? "-";
        public string GpuVendorText => _staticInfo != null ? HardwareInfoFormatter.FormatVendorName(_staticInfo.VendorId) : "-";
        public string GpuDriverVersionText => _staticInfo?.DriverVersion ?? "-";
        public string GpuDedicatedMemoryText => _staticInfo != null ? HardwareInfoFormatter.FormatBytesAsGb(_staticInfo.DedicatedVideoMemoryBytes) : "-";
        public string GpuSharedMemoryText => _staticInfo != null ? HardwareInfoFormatter.FormatBytesAsGb(_staticInfo.SharedSystemMemoryBytes) : "-";
        public string GpuDeviceIdText => _staticInfo != null ? HardwareInfoFormatter.FormatPciId(_staticInfo.DeviceId) : "-";
        public string GpuPnpDeviceIdText => _staticInfo?.PnpDeviceId ?? "-";


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}