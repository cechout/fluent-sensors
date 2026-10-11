using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance.Lhm
{
    // one fact of the battery info panel; the label is the LHM sensor name, never translated
    public class LhmBatteryFact : INotifyPropertyChanged
    {
        public LhmBatteryFact(string name)
        {
            Name = name;
        }

        public string Name { get; }

        private string _valueText = "-";
        public string ValueText
        {
            get => _valueText;
            set
            {
                if (_valueText == value) return;
                _valueText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueText)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    // one battery:
    // a data holder, LhmBatteryPerformanceViewModel parses; a graph is null while LHM has its sensor deactivated (no
    // rate without a reading from the firmware)
    public class LhmBatteryInstanceViewModel : INotifyPropertyChanged
    {
        // === constructor ===

        public LhmBatteryInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;

            DetailOptions = new ObservableCollection<SensorSwitchCandidate>();
            Facts = new ObservableCollection<LhmBatteryFact>();
        }


        // === bindable properties ===

        public string HardwareName { get; }

        private SensorGraphViewModel _chargeLevel;
        public SensorGraphViewModel ChargeLevel
        {
            get => _chargeLevel;
            set { _chargeLevel = value; OnPropertyChanged(); }
        }

        // charge or discharge power; LHM reports the amount and names the direction ("Charge Rate", "Discharge Rate")
        private SensorGraphViewModel _rate;
        public SensorGraphViewModel Rate
        {
            get => _rate;
            set { _rate = value; OnPropertyChanged(); }
        }

        // voltage, current, remaining capacity, remaining time or temperature
        private SensorGraphViewModel _detail;
        public SensorGraphViewModel Detail
        {
            get => _detail;
            set
            {
                if (_detail == value) return;
                _detail = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "Detail", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> DetailOptions { get; }

        internal void SetDetailWithoutPersisting(SensorGraphViewModel value)
        {
            _detail = value;
            OnPropertyChanged(nameof(Detail));
        }

        // info panel; the readings that only move with the age of the battery (designed and full capacity, degradation)
        public ObservableCollection<LhmBatteryFact> Facts { get; }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
