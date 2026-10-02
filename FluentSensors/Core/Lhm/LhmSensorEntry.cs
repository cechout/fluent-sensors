using System.ComponentModel;
using System.Runtime.CompilerServices;


namespace FluentSensors.Core.Lhm
{
    // one LHM sensor:
    // Id, Name, SensorType and a bindable Value; thresholds, statistics and hiding belong to the pages
    public class LhmSensorEntry : INotifyPropertyChanged
    {
        public string Id { get; }
        public string Name { get; }
        public string SensorType { get; }

        public LhmSensorEntry(string id, string name, string sensorType)
        {
            Id = id;
            Name = name;
            SensorType = sensorType;
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
