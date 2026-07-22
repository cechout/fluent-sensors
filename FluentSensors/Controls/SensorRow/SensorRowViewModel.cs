using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Common;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Controls.SensorRow
{
    public class SensorRowViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // mathematical fields for internal calculations
        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        private double _sum = 0;
        private int _count = 0;
        private double _currentRaw;
        private double _avg;
        private DispatcherQueue _dispatcherQueue;


        // === constructor ===

        public SensorRowViewModel()
        {
            SettingsService.Instance.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged(string newTheme)
        {
            if (_dispatcherQueue != null)
                _dispatcherQueue.TryEnqueue(RecalculateColors);
            else
                RecalculateColors();
        }


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
                InitializeThreshold();
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
                    "Throughput" => "MB/s",
                    "Fan" => "RPM",
                    _ => "" // fallback, if LibreHardwareMonitor sends something exotic
                };
            }
        }

        // threshold, owned by the shared editor; created once Id is set (see InitializeThreshold), null before that
        public ThresholdEditorViewModel Threshold { get; private set; }

        // threshold indicator (small badge in SensorRowControl)
        private string _thresholdIndicatorText = "-";
        public string ThresholdIndicatorText
        {
            get => _thresholdIndicatorText;
            set { _thresholdIndicatorText = value; OnPropertyChanged(); }
        }
        private Brush _thresholdIndicatorBrush = new SolidColorBrush(Colors.Transparent);
        public Brush ThresholdIndicatorBrush
        {
            get => _thresholdIndicatorBrush;
            set { _thresholdIndicatorBrush = value; OnPropertyChanged(); }
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
        private Brush _currentValueColor = DefaultTextColor.Resolve();
        public Brush CurrentValueColor
        {
            get => _currentValueColor;
            set { _currentValueColor = value; OnPropertyChanged(); }
        }
        private Brush _minimumValueColor = DefaultTextColor.Resolve();
        public Brush MinimumValueColor
        {
            get => _minimumValueColor;
            set { _minimumValueColor = value; OnPropertyChanged(); }
        }
        private Brush _maximumValueColor = DefaultTextColor.Resolve();
        public Brush MaximumValueColor
        {
            get => _maximumValueColor;
            set { _maximumValueColor = value; OnPropertyChanged(); }
        }
        private Brush _averageValueColor = DefaultTextColor.Resolve();
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

        // reset stats method
        public void ResetMinMax()
        {
            _min = double.MaxValue;
            _max = double.MinValue;
            _sum = 0;
            _count = 0;

            MinimumValue = "-";
            MaximumValue = "-";
            AverageValue = "-";

            MinimumValueColor = DefaultTextColor.Resolve();
            MaximumValueColor = DefaultTextColor.Resolve();
            AverageValueColor = DefaultTextColor.Resolve();
        }

        // unsubscribes from SettingsService and the threshold editor; must be called once this row is permanently
        // removed (not just moved to the hidden list), or it keeps reacting to theme/threshold changes after disposal
        public void Cleanup()
        {
            SettingsService.Instance.ThemeChanged -= OnThemeChanged;
            Threshold?.Cleanup();
            if (Threshold != null) Threshold.PropertyChanged -= OnThresholdPropertyChanged;
        }


        // === private helpers ===

        // creates this rows threshold editor once both Id and SensorType are known; SensorType must be set before Id in
        // the object initializer (SensorsViewModel), otherwise the editor would fall back to the generic default profile
        private void InitializeThreshold()
        {
            if (string.IsNullOrEmpty(_id) || Threshold != null) return;

            // captures the UI thread this row was created on, so theme changes can be marshalled back here safely
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            Threshold = new ThresholdEditorViewModel(_id, _sensorType);
            Threshold.PropertyChanged += OnThresholdPropertyChanged;
            RecalculateColors();
            UpdateThresholdIndicator();
        }

        private void OnThresholdPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ThresholdEditorViewModel.IsEnabled) ||
                e.PropertyName == nameof(ThresholdEditorViewModel.Value) ||
                e.PropertyName == nameof(ThresholdEditorViewModel.Direction) ||
                e.PropertyName == nameof(ThresholdEditorViewModel.Color))
            {
                RecalculateColors();
                UpdateThresholdIndicator();
            }
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
            if (Threshold == null || !Threshold.IsBreached(value))
                return DefaultTextColor.Resolve();

            return new SolidColorBrush(Threshold.Color);
        }

        // updates the small threshold badge shown in the new column
        private void UpdateThresholdIndicator()
        {
            if (Threshold != null && Threshold.IsEnabled)
            {
                ThresholdIndicatorText = $"{Threshold.Value:0}";
                ThresholdIndicatorBrush = Threshold.ColorBrush;
            }
            else
            {
                ThresholdIndicatorText = "--";
                ThresholdIndicatorBrush = new SolidColorBrush(Colors.Transparent);
            }
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}