using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance.Lhm
{
    // one entry per detected drive
    // A plain data holder; all sensor discovery/parsing lives in LhmStoragePerformanceViewModel instead
    public class LhmStorageInstanceViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // best-effort match against WMI-reported drives; see HardwareNameMatcher for the matching approach and
        // its limitations; null if no candidate matched at all
        private readonly WinStorageDriveInfo _staticInfo;


        // === constructor ===

        public LhmStorageInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;

            _staticInfo = HardwareNameMatcher.FindBestMatch(
                hardwareName,
                WinStaticInfoService.Instance.Drives,
                drive => drive.FriendlyName);

            TotalActivityOptions = new ObservableCollection<SensorSwitchCandidate>();
            ReadRateOptions = new ObservableCollection<SensorSwitchCandidate>();
            WriteRateOptions = new ObservableCollection<SensorSwitchCandidate>();
        }


        // === bindable properties ===

        public string HardwareName { get; }

        // public setter persists the choice, SetXWithoutPersisting is for the default/restored graph during
        // discovery; ReadRate/WriteRate further below follow the same shape silently
        private SensorGraphViewModel _totalActivity;
        public SensorGraphViewModel TotalActivity
        {
            get => _totalActivity;
            set
            {
                if (_totalActivity == value) return;
                _totalActivity = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "TotalActivity", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> TotalActivityOptions { get; }

        internal void SetTotalActivityWithoutPersisting(SensorGraphViewModel value)
        {
            _totalActivity = value;
            OnPropertyChanged(nameof(TotalActivity));
        }

        // not charted, just a Y-max helper for TotalActivity (covers both its Total Activity % and Free Space GB
        // candidates, see StorageDetailView.xaml)
        // the drives own reported total, no rounding needed
        private double _totalSpace;
        public double TotalSpace
        {
            get => _totalSpace;
            set { _totalSpace = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _writeRate;
        public SensorGraphViewModel WriteRate
        {
            get => _writeRate;
            set
            {
                if (_writeRate == value) return;
                _writeRate = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "Write", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> WriteRateOptions { get; }

        internal void SetWriteRateWithoutPersisting(SensorGraphViewModel value)
        {
            _writeRate = value;
            OnPropertyChanged(nameof(WriteRate));
        }

        private SensorGraphViewModel _readRate;
        public SensorGraphViewModel ReadRate
        {
            get => _readRate;
            set
            {
                if (_readRate == value) return;
                _readRate = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "Read", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> ReadRateOptions { get; }

        internal void SetReadRateWithoutPersisting(SensorGraphViewModel value)
        {
            _readRate = value;
            OnPropertyChanged(nameof(ReadRate));
        }


        // === static info text properties ===

        // read-only, purely computed from the matched WinStorageDriveInfo
        public string StorageFriendlyNameText => _staticInfo?.FriendlyName ?? "-";
        public string StorageSerialNumberText => _staticInfo?.SerialNumber ?? "-";
        public string StorageFirmwareRevisionText => _staticInfo?.FirmwareRevision ?? "-";
        public string StorageBusTypeText => _staticInfo?.BusType ?? "-";
        public string StorageSizeText => _staticInfo != null ? HardwareInfoFormatter.FormatBytesAsGb(_staticInfo.SizeBytes) : "-";
        public string StoragePnpDeviceIdText => _staticInfo?.PnpDeviceId ?? "-";
        public string StorageManufactureDateText => !string.IsNullOrEmpty(_staticInfo?.ManufactureDate) ? _staticInfo.ManufactureDate : "-";

        // everything below goes through HardwareInfoFormatter, which itself returns "-" per field when Windows
        // didnt report it - see the doc comment on WinStorageDriveInfo for why these are nullable to begin with
        public string StorageTemperatureText => HardwareInfoFormatter.FormatCelsius(_staticInfo?.TemperatureCelsius);
        public string StorageTemperatureMaxText => HardwareInfoFormatter.FormatCelsius(_staticInfo?.TemperatureMaxCelsius);
        public string StorageWearText => HardwareInfoFormatter.FormatPercent(_staticInfo?.WearPercent);
        public string StoragePowerOnHoursText => HardwareInfoFormatter.FormatHours(_staticInfo?.PowerOnHours);
        public string StorageReadErrorsText => HardwareInfoFormatter.FormatErrorCounts(_staticInfo?.ReadErrorsTotal, _staticInfo?.ReadErrorsCorrected, _staticInfo?.ReadErrorsUncorrected);
        public string StorageWriteErrorsText => HardwareInfoFormatter.FormatErrorCounts(_staticInfo?.WriteErrorsTotal, _staticInfo?.WriteErrorsCorrected, _staticInfo?.WriteErrorsUncorrected);
        public string StorageStartStopCyclesText => HardwareInfoFormatter.FormatCycleCount(_staticInfo?.StartStopCycleCount, _staticInfo?.StartStopCycleCountMax);
        public string StorageLoadUnloadCyclesText => HardwareInfoFormatter.FormatCycleCount(_staticInfo?.LoadUnloadCycleCount, _staticInfo?.LoadUnloadCycleCountMax);
        public string StorageLatencyMaxText => HardwareInfoFormatter.FormatLatencyTriple(_staticInfo?.ReadLatencyMaxMs, _staticInfo?.WriteLatencyMaxMs, _staticInfo?.FlushLatencyMaxMs);


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
