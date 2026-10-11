using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance.Lhm
{
    // one drive:
    // a data holder, LhmStoragePerformanceViewModel parses
    public class LhmStorageInstanceViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // best-effort match against the WMI drives (see HardwareNameMatcher); null without a match
        private WinStorageDriveInfo _staticInfo;


        // === constructor ===

        public LhmStorageInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;

            _staticInfo = MatchStaticInfo();

            // until Detach
            WinStaticInfoService.Instance.DrivesChanged += OnStaticInfoChanged;

            TotalActivityOptions = new ObservableCollection<SensorSwitchCandidate>();
            ReadRateOptions = new ObservableCollection<SensorSwitchCandidate>();
            WriteRateOptions = new ObservableCollection<SensorSwitchCandidate>();
        }


        // === bindable properties ===

        public string HardwareName { get; }

        // the public setter persists the choice, SetXWithoutPersisting is for the default or restored graph;
        // ReadRate and WriteRate alike
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

        // not charted; the TotalActivity y-max for its Free Space candidate, the reported total
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

        // static info from the matched WinStorageDriveInfo
        public string StorageFriendlyNameText => _staticInfo?.FriendlyName ?? "-";
        public string StorageSerialNumberText => _staticInfo?.SerialNumber ?? "-";
        public string StorageFirmwareRevisionText => _staticInfo?.FirmwareRevision ?? "-";
        public string StorageBusTypeText => _staticInfo?.BusType ?? "-";
        public string StorageSizeText => _staticInfo != null ? HardwareInfoFormatter.FormatBytesAsGb(_staticInfo.SizeBytes) : "-";
        public string StoragePnpDeviceIdText => _staticInfo?.PnpDeviceId ?? "-";
        public string StorageManufactureDateText => !string.IsNullOrEmpty(_staticInfo?.ManufactureDate) ? _staticInfo.ManufactureDate : "-";

        // the performance page name, the model over the raw LHM name; HardwareName stays raw everywhere else
        public string PerformanceDisplayName => _staticInfo?.FriendlyName ?? HardwareName;

        // through HardwareInfoFormatter, "-" per field Windows did not report
        public string StorageTemperatureText => HardwareInfoFormatter.FormatCelsius(_staticInfo?.TemperatureCelsius);
        public string StorageTemperatureMaxText => HardwareInfoFormatter.FormatCelsius(_staticInfo?.TemperatureMaxCelsius);
        public string StorageWearText => HardwareInfoFormatter.FormatPercent(_staticInfo?.WearPercent);
        public string StoragePowerOnHoursText => HardwareInfoFormatter.FormatHours(_staticInfo?.PowerOnHours);
        public string StorageReadErrorsText => HardwareInfoFormatter.FormatErrorCounts(_staticInfo?.ReadErrorsTotal, _staticInfo?.ReadErrorsCorrected, _staticInfo?.ReadErrorsUncorrected);
        public string StorageWriteErrorsText => HardwareInfoFormatter.FormatErrorCounts(_staticInfo?.WriteErrorsTotal, _staticInfo?.WriteErrorsCorrected, _staticInfo?.WriteErrorsUncorrected);
        public string StorageStartStopCyclesText => HardwareInfoFormatter.FormatCycleCount(_staticInfo?.StartStopCycleCount, _staticInfo?.StartStopCycleCountMax);
        public string StorageLoadUnloadCyclesText => HardwareInfoFormatter.FormatCycleCount(_staticInfo?.LoadUnloadCycleCount, _staticInfo?.LoadUnloadCycleCountMax);
        public string StorageLatencyMaxText => HardwareInfoFormatter.FormatLatencyTriple(_staticInfo?.ReadLatencyMaxMs, _staticInfo?.WriteLatencyMaxMs, _staticInfo?.FlushLatencyMaxMs);


        // === public methods ===

        // the hardware left and this instance with it
        public void Detach()
        {
            WinStaticInfoService.Instance.DrivesChanged -= OnStaticInfoChanged;
        }


        // === event handlers ===

        // Windows was asked again (a drive was plugged in, the reliability counters moved); every static text re-reads
        private void OnStaticInfoChanged()
        {
            _staticInfo = MatchStaticInfo();

            foreach (string propertyName in StaticInfoPropertyNames) OnPropertyChanged(propertyName);
        }

        private static readonly string[] StaticInfoPropertyNames =
        {
            nameof(StorageFriendlyNameText), nameof(StorageSerialNumberText), nameof(StorageFirmwareRevisionText),
            nameof(StorageBusTypeText), nameof(StorageSizeText), nameof(StoragePnpDeviceIdText),
            nameof(StorageManufactureDateText), nameof(PerformanceDisplayName), nameof(StorageTemperatureText),
            nameof(StorageTemperatureMaxText), nameof(StorageWearText), nameof(StoragePowerOnHoursText),
            nameof(StorageReadErrorsText), nameof(StorageWriteErrorsText), nameof(StorageStartStopCyclesText),
            nameof(StorageLoadUnloadCyclesText), nameof(StorageLatencyMaxText)
        };


        // === private helpers ===

        private WinStorageDriveInfo MatchStaticInfo() => HardwareNameMatcher.FindBestMatch(
            HardwareName,
            WinStaticInfoService.Instance.Drives,
            drive => drive.FriendlyName);


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
