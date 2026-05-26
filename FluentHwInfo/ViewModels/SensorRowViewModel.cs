using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluentHwInfo.ViewModels
{
    /// <summary>
    /// Serves as the granular DataContext directly bound to a single UI row, representing the smallest nested scope in the 
    /// ViewModel hierarchy.
    /// Acts as a pure "Child-ViewModel" which is entirely managed by its parent (HardwareGroupViewModel).
    /// 
    /// Responsibilities:
    /// - Stores the current, minimum, maximum, and average values as formatted, bindable strings for the XAML UI.
    /// - Calculates the statistical values (Min, Max, Avg) internally whenever a new raw double value is passed via the 
    ///   UpdateValue(double) method.
    /// - Implements INotifyPropertyChanged to automatically trigger UI redraws when values change.
    /// 
    /// Architecture Constraints:
    /// This class is strictly decoupled from hardware services. It does NOT pull data itself. Instead, it passively waits for 
    /// the Parent-ViewModel to push raw data into it.
    /// </summary>

    // SensorRowViewModel inherits from INotifyPropertyChanged
    // INotifyPropertyChanged is mandatory for the UI to react to changes in the data,
    // otherwise the UI would never know that it should update itself
    public class SensorRowViewModel : INotifyPropertyChanged
    {
        private string _currentValue = "-";
        private string _minimumValue = "-";
        private string _maximumValue = "-";
        private string _averageValue = "-";

        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        private double _sum = 0;
        private int _count = 0;

        // the internal storage for the unit (e.g., "W" or "°C")
        private string _unit = "";

        public string Id { get; set; }
        public string Name { get; set; } = "Unknown Sensor";
        public string SensorType
        {
            set
            {
                _unit = value switch
                {
                    "Temperature" => "°C",
                    "Power" => "W",
                    "Load" => "%",
                    "Clock" => "MHz",
                    "SmallData" => "MB",
                    "Data" => "GB",
                    "Voltage" => "V",
                    "Fan" => "RPM",
                    _ => "" // fallback, if LibreHardwareMonitor sends something exotic
                };
            }
        }
        public string CurrentValue
        {
            get => _currentValue;
            set
            {
                _currentValue = value;
                OnPropertyChanged();
            }
        }
        public string MinimumValue
        {
            get => _minimumValue;
            set
            {
                _minimumValue = value;
                OnPropertyChanged();
            }
        }
        public string MaximumValue
        {
            get => _maximumValue;
            set
            {
                _maximumValue = value;
                OnPropertyChanged();
            }
        }
        public string AverageValue
        {
            get => _averageValue;
            set
            {
                _averageValue = value;
                OnPropertyChanged();
            }
        }

        public void UpdateValue(double newValue)
        {
            // 1. mathematics
            if (newValue < _min) _min = newValue;
            if (newValue > _max) _max = newValue;

            _sum += newValue;
            _count++;
            double avg = _sum / _count;

            // 2. build strings for the UI with the dynamic unit
            CurrentValue = $"{newValue:0.0} {_unit}";
            MinimumValue = $"{_min:0.0} {_unit}";
            MaximumValue = $"{_max:0.0} {_unit}";
            AverageValue = $"{avg:0.0} {_unit}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}