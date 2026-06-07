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
        // core configuration properties for the sensor row
        public string Id { get; set; }
        public string Name { get; set; } = "Unknown Sensor";
        private string _unit = "";
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


        // item state
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }


        // mathematical fields for internal calculations
        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        private double _sum = 0;
        private int _count = 0;


        // formatted string properties for the ui
        private string _currentValue = "-";
        public string CurrentValue
        {
            get => _currentValue;
            set
            {
                _currentValue = value;
                OnPropertyChanged();
            }
        }
        private string _minimumValue = "-";
        public string MinimumValue
        {
            get => _minimumValue;
            set
            {
                _minimumValue = value;
                OnPropertyChanged();
            }
        }
        private string _maximumValue = "-";
        public string MaximumValue
        {
            get => _maximumValue;
            set
            {
                _maximumValue = value;
                OnPropertyChanged();
            }
        }
        private string _averageValue = "-";
        public string AverageValue
        {
            get => _averageValue;
            set
            {
                _averageValue = value;
                OnPropertyChanged();
            }
        }

        
        // data processing
        public void UpdateValue(double newValue)
        {
            if (newValue < _min) _min = newValue;
            if (newValue > _max) _max = newValue;

            _sum += newValue;
            _count++;
            double avg = _sum / _count;

            // build strings for the UI with the dynamic unit
            CurrentValue = $"{newValue:0.0} {_unit}";
            MinimumValue = $"{_min:0.0} {_unit}";
            MaximumValue = $"{_max:0.0} {_unit}";
            AverageValue = $"{avg:0.0} {_unit}";
        }


        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}