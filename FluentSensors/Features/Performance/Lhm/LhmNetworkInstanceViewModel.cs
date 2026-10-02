using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance.Lhm
{
    // one network adapter:
    // an active one (HardwareMonitorService drops inactive ones); a data holder, LhmNetworkPerformanceViewModel parses
    public class LhmNetworkInstanceViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // best-effort match against the WMI adapters (see HardwareNameMatcher); null without a match
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

        // utilization in percent or the cumulative Data Uploaded and Downloaded in GB
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

        // static info from the matched WinNetworkAdapterInfo; the hardware description ("Intel(R) Wi-Fi 6E AX211
        // 160MHz"), the connection name ("WLAN") only serves the matching
        public string NetworkNameText => _staticInfo?.Description ?? "-";
        public string NetworkMacAddressText => _staticInfo != null ? HardwareInfoFormatter.FormatMacAddress(_staticInfo.MacAddress) : "-";
        public string NetworkSpeedText => _staticInfo != null ? HardwareInfoFormatter.FormatBitsPerSecond(_staticInfo.SpeedBitsPerSecond) : "-";
        public string NetworkInterfaceTypeText => _staticInfo != null ? HardwareInfoFormatter.FormatInterfaceType(_staticInfo.InterfaceType) : "-";
        public string NetworkIPv4AddressesText => _staticInfo != null ? HardwareInfoFormatter.FormatIpAddresses(_staticInfo.IPv4Addresses) : "-";
        public string NetworkIPv6AddressesText => _staticInfo != null ? HardwareInfoFormatter.FormatIpAddresses(_staticInfo.IPv6Addresses) : "-";
        public string NetworkDhcpEnabledText => _staticInfo != null ? HardwareInfoFormatter.FormatYesNo(_staticInfo.DhcpEnabled) : "-";

        // the performance page name, the description over the raw connection name ("WLAN", "Ethernet");
        // HardwareName stays raw everywhere else
        public string PerformanceDisplayName => _staticInfo?.Description ?? HardwareName;

        // the header and tile glyph; wi-fi for a wireless adapter, see HardwareGroupInfo.GetNetworkIconGlyph
        public string IconGlyph => HardwareGroupInfo.GetNetworkIconGlyph(_staticInfo?.InterfaceType);


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
