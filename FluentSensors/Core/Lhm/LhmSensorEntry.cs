using System.ComponentModel;
using System.Runtime.CompilerServices;


namespace FluentSensors.Core.Lhm
{
    // minimal live data node for one LHM sensor:
    // static identity (Id/Name/SensorType) plus a bindable Value no threshold
    // no min/max/avg, no hide/show state; those stay page-specific concerns on top of this
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
                // no equality guard: every payload tick must raise PropertyChanged, even with an unchanged value,
                // since consumers (e.g. SensorRowViewModel) count every tick for their own Min/Max/Avg stats
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