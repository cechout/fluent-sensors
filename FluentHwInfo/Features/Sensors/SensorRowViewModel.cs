using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using FluentHwInfo.Controls;
using FluentHwInfo.Persistence.Models;
using FluentHwInfo.Persistence.Services;


namespace FluentHwInfo.Features.Sensors
{
    public class SensorRowViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // mathematical fields for internal calculations
        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        private double _sum = 0;
        private int _count = 0;

        // threshold tracking
        private bool _isSubscribedToThreshold;
        private DispatcherQueue _dispatcherQueue;
        private SensorThreshold _threshold;
        private double _currentRaw;
        private double _avg;


        // === bindable properties ===

        // core configuration properties for the sensor row
        private string _id;
        public string Id
        {
            get => _id;
            set
            {
                if (_id == value) return; // Id is set once via object initializer; guards against double-subscribing
                _id = value;
                OnPropertyChanged();
                SubscribeToThreshold();
            }
        }
        public string Name { get; set; } = "Unknown Sensor";
        public int SortOrder { get; set; } // original creation order
        public string Unit { get; private set; } = "";
        private string _sensorType = "";
        public string SensorType
        {
            get => _sensorType;
            set
            {
                _sensorType = value;
                Unit = value switch
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

                    // persist immediately so the checkbox state survives an app restart; Id is always set by this point
                    // since object initializers set it before IsSelected
                    if (!string.IsNullOrEmpty(_id))
                    {
                        SensorStateService.Instance.SetSelected(_id, value);
                    }
                }
            }
        }
        private bool _isHidden;
        public bool IsHidden
        {
            get => _isHidden;
            set
            {
                if (_isHidden != value)
                {
                    _isHidden = value;
                    OnPropertyChanged();
                }
            }
        }
        private bool _isDisabled;
        public bool IsDisabled
        {
            get => _isDisabled;
            set
            {
                if (_isDisabled != value)
                {
                    _isDisabled = value;
                    OnPropertyChanged();
                }
            }
        }

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

        // text color properties
        private static Brush DefaultTextBrush => (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        private Brush _currentValueColor = DefaultTextBrush;
        public Brush CurrentValueColor
        {
            get => _currentValueColor;
            set { _currentValueColor = value; OnPropertyChanged(); }
        }
        private Brush _minimumValueColor = DefaultTextBrush;
        public Brush MinimumValueColor
        {
            get => _minimumValueColor;
            set { _minimumValueColor = value; OnPropertyChanged(); }
        }
        private Brush _maximumValueColor = DefaultTextBrush;
        public Brush MaximumValueColor
        {
            get => _maximumValueColor;
            set { _maximumValueColor = value; OnPropertyChanged(); }
        }
        private Brush _averageValueColor = DefaultTextBrush;
        public Brush AverageValueColor
        {
            get => _averageValueColor;
            set { _averageValueColor = value; OnPropertyChanged(); }
        }


        // === public methods ===

        public void UpdateValue(double newValue)
        {
            if (newValue < _min) _min = newValue;
            if (newValue > _max) _max = newValue;

            _sum += newValue;
            _count++;
            _currentRaw = newValue; 
            _avg = _sum / _count;

            // build strings for the UI with the dynamic unit
            CurrentValue = $"{newValue:0.0} {Unit}";
            MinimumValue = $"{_min:0.0} {Unit}";
            MaximumValue = $"{_max:0.0} {Unit}";
            AverageValue = $"{_avg:0.0} {Unit}";

            RecalculateColors();
        }

        // resets method
        public void ResetMinMax()
        {
            _min = double.MaxValue;
            _max = double.MinValue;
            _sum = 0;
            _count = 0;

            MinimumValue = "-";
            MaximumValue = "-";
            AverageValue = "-";

            MinimumValueColor = DefaultTextBrush;
            MaximumValueColor = DefaultTextBrush;
            AverageValueColor = DefaultTextBrush;
        }


        // === private helpers ===

        // threshold handling
        private void SubscribeToThreshold()
        {
            if (string.IsNullOrEmpty(_id) || _isSubscribedToThreshold) return;

            // captures the UI thread this row was created on, so threshold updates can be marshalled back here safely
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _isSubscribedToThreshold = true;

            SensorStateService.Instance.StateChanged += OnStateChanged;
            ApplyThreshold(SensorStateService.Instance.GetState(_id).Threshold);
        }

        private void OnStateChanged(string sensorId, SensorState state)
        {
            if (sensorId != _id) return;
            _dispatcherQueue.TryEnqueue(() => ApplyThreshold(state.Threshold));
        }

        private void ApplyThreshold(SensorThreshold threshold)
        {
            _threshold = threshold;
            RecalculateColors();
        }

        // color evaluation
        private void RecalculateColors()
        {
            if (_count == 0) return; // no values received yet, nothing to color

            CurrentValueColor = EvaluateColor(_currentRaw);
            MinimumValueColor = EvaluateColor(_min);
            MaximumValueColor = EvaluateColor(_max);
            AverageValueColor = EvaluateColor(_avg);
        }

        private Brush EvaluateColor(double value)
        {
            if (_threshold == null || !_threshold.IsEnabled)
                return DefaultTextBrush;

            bool isBreached = _threshold.Direction == ThresholdDirection.Above
                ? value > _threshold.Value
                : value < _threshold.Value;

            return isBreached ? new SolidColorBrush(_threshold.Color) : DefaultTextBrush;
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}