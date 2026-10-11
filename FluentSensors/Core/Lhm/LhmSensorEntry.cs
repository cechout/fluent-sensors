using System.ComponentModel;
using System.Runtime.CompilerServices;


namespace FluentSensors.Core.Lhm
{
    // one LHM sensor:
    // Id, Name, SensorType and a bindable Value; thresholds, statistics and hiding belong to the pages
    public class LhmSensorEntry : INotifyPropertyChanged
    {
        public string Id { get; }
        public string SensorType { get; }

        public LhmSensorEntry(string id, string name, string sensorType)
        {
            Id = id;
            _name = name;
            SensorType = sensorType;
        }

        // LHM renames a few sensors while they run (the battery rate reads "Charge Rate" or "Discharge Rate")
        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                OnPropertyChanged();
            }
        }

        // false while LHM has the sensor deactivated (a battery on mains reports no remaining time); the entry stays in
        // its instance and Value keeps the last reading, so only a consumer that lists sensors has to react
        private bool _isActive = true;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                OnPropertyChanged();
            }
        }

        private double _value;
        public double Value
        {
            get => _value;
            set
            {
                // no equality guard; SensorRowViewModel counts every tick for its statistics, unchanged ones too
                _value = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
