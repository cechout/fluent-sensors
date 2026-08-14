using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance.Lhm
{
    // one entry per currently active network adapter (inactive adapters never reach this VM at all, since
    // HardwareMonitorService already excludes them from the payload)
    // A plain data holder; all sensor discovery/parsing lives in LhmNetworkPerformanceViewModel instead
    public class LhmNetworkInstanceViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // best-effort match against WMI-reported adapters; see HardwareNameMatcher for the matching approach
        // and its limitations; null if no candidate matched at all
        private readonly WinNetworkAdapterInfo _staticInfo;


        // === constructor ===

        public LhmNetworkInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;

            _staticInfo = HardwareNameMatcher.FindBestMatch(
                hardwareName,
                WinStaticInfoService.Instance.NetworkAdapters,
                adapter => adapter.Name);

            NetworkUtilizationOptions = new ObservableCollection<SensorSwitchCandidate>();
        }


        // === bindable properties ===

        public string HardwareName { get; }

        private SensorGraphViewModel _uploadSpeed;
        public SensorGraphViewModel UploadSpeed
        {
            get => _uploadSpeed;
            set { _uploadSpeed = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _downloadSpeed;
        public SensorGraphViewModel DownloadSpeed
        {
            get => _downloadSpeed;
            set { _downloadSpeed = value; OnPropertyChanged(); }
        }

        // switches between live utilization % and the two cumulative Data Uploaded/Downloaded GB counters
        private SensorGraphViewModel _networkUtilization;
        public SensorGraphViewModel NetworkUtilization
        {
            get => _networkUtilization;
            set
            {
                if (_networkUtilization == value) return;
                _networkUtilization = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "Utilization", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> NetworkUtilizationOptions { get; }

        internal void SetNetworkUtilizationWithoutPersisting(SensorGraphViewModel value)
        {
            _networkUtilization = value;
            OnPropertyChanged(nameof(NetworkUtilization));
        }


        // === static info text properties ===

        // read-only, purely computed from the matched WinNetworkAdapterInfo
        // NetworkNameText intentionally surfaces the hardware description (e.g. "Intel(R) Wi-Fi 6E AX211 160MHz"),
        // not the OS connection name (e.g. "WLAN") - the connection name is only meaningful internally for the
        // LHM/WMI name-matching in the constructor above, not as something to show the user
        public string NetworkNameText => _staticInfo?.Description ?? "-";
        public string NetworkMacAddressText => _staticInfo != null ? HardwareInfoFormatter.FormatMacAddress(_staticInfo.MacAddress) : "-";
        public string NetworkSpeedText => _staticInfo != null ? HardwareInfoFormatter.FormatBitsPerSecond(_staticInfo.SpeedBitsPerSecond) : "-";
        public string NetworkInterfaceTypeText => _staticInfo != null ? HardwareInfoFormatter.FormatInterfaceType(_staticInfo.InterfaceType) : "-";
        public string NetworkIPv4AddressesText => _staticInfo != null ? HardwareInfoFormatter.FormatIpAddresses(_staticInfo.IPv4Addresses) : "-";
        public string NetworkIPv6AddressesText => _staticInfo != null ? HardwareInfoFormatter.FormatIpAddresses(_staticInfo.IPv6Addresses) : "-";
        public string NetworkDhcpEnabledText => _staticInfo != null ? HardwareInfoFormatter.FormatYesNo(_staticInfo.DhcpEnabled) : "-";


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
