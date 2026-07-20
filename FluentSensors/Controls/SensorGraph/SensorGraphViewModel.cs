using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using FluentSensors.Controls;
using FluentSensors.Persistence.Models;
using FluentSensors.Persistence.Services;
using FluentSensors.Common;


namespace FluentSensors.Controls.SensorGraph
{
    public class SensorGraphViewModel : INotifyPropertyChanged
    {
        // === fields ===

        private double _currentRaw;
        private readonly double _thresholdStep;
        private readonly double _yMaxStep;


        // === constructor ===

        public SensorGraphViewModel(string sensorId, string sensorName, string sensorType)
        {
            SensorId = sensorId;
            SensorName = sensorName;
            CurrentValueText = "-"; // placeholder text until we have the first value
            CurrentValueColor = DefaultTextColor.Resolve();

            // this raw data list will be plotted by LiveCharts
            // we use LINQ Enumerable.Repeat to fill the entire list with "0.0" values at startup
            SensorData = new ObservableCollection<double?>(Enumerable.Repeat<double?>(0.0, SettingsService.Instance.GraphDataPoints));

            GraphColor = ResolveGraphColor(SettingsService.Instance.UseGraphAccentColor, SettingsService.Instance.GraphCustomColor);
            SettingsService.Instance.GraphColorChanged += OnGraphColorChanged;
            SettingsService.Instance.GraphDataPointsChanged += OnGraphDataPointsChanged;
            SettingsService.Instance.ThemeChanged += OnThemeChanged;

            // per-sensor-type starting values - a clock sensor needs a much higher threshold/step than a load percentage
            var profile = SensorTypeProfiles.GetProfile(sensorType);
            _thresholdStep = profile.ThresholdStep;
            _yMaxStep = profile.YMaxStep;

            // restore this sensors full state if it was already configured before (e.g. previously pinned, or loaded from
            // disk at startup); a null Value/ManualYMax means the user never touched it yet, so we fall back to this
            // sensor types default instead of a generic one
            var existingState = SensorStateService.Instance.GetState(SensorId);
            _isThresholdEnabled = existingState.Threshold.IsEnabled;
            _manualThreshold = existingState.Threshold.Value ?? profile.ThresholdDefault;
            _thresholdDirection = existingState.Threshold.Direction;
            _thresholdColor = existingState.Threshold.Color;
            _isAutoScaled = existingState.IsAutoScaled;
            _manualYMax = existingState.ManualYMax ?? profile.YMaxDefault;

            UpdateYMaxDisplay();
        }


        // === bindable properties ===

        // general
        public ObservableCollection<double?> SensorData { get; private set; }
        public string SensorId { get; }
        private string _sensorName = "not provided";
        public string SensorName
        {
            get => _sensorName;
            set { _sensorName = value; OnPropertyChanged(); }
        }
        private string _currentValueText = "-";
        public string CurrentValueText
        {
            get => _currentValueText;
            set { _currentValueText = value; OnPropertyChanged(); }
        }
        private Windows.UI.Color _graphColor;
        public Windows.UI.Color GraphColor
        {
            get => _graphColor;
            private set { _graphColor = value; OnPropertyChanged(); }
        }

        // y-axis
        private bool _isAutoScaled = true;
        public bool IsAutoScaled
        {
            get => _isAutoScaled;
            set
            {
                if (_isAutoScaled != value)
                {
                    _isAutoScaled = value;
                    OnPropertyChanged();
                    UpdateYMaxDisplay();
                }
            }
        }
        private double _manualYMax = 100; // initial value for manual y-axis max
        public double ManualYMax
        {
            get => _manualYMax;
            set
            {
                if (_manualYMax != value)
                {
                    _manualYMax = value;
                    OnPropertyChanged();
                    UpdateYMaxDisplay();
                }
            }
        }
        private string _actualYMaxText = "100";
        public string ActualYMaxText
        {
            get => _actualYMaxText;
            set
            {
                if (_actualYMaxText != value)
                {
                    _actualYMaxText = value;
                    OnPropertyChanged();
                }
            }
        }

        // threshold configuration
        private bool _isThresholdEnabled = false;
        public bool IsThresholdEnabled
        {
            get => _isThresholdEnabled;
            set
            {
                if (_isThresholdEnabled != value)
                {
                    _isThresholdEnabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThresholdValue));
                    PushStateToService();
                    RecalculateColor();
                }
            }
        }
        private double _manualThreshold = 50;
        public double ManualThreshold
        {
            get => _manualThreshold;
            set
            {
                if (_manualThreshold != value)
                {
                    _manualThreshold = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThresholdValue));
                    PushStateToService();
                    RecalculateColor();
                }
            }
        }
        public double? ThresholdValue => IsThresholdEnabled ? _manualThreshold : (double?)null;
        private ThresholdDirection _thresholdDirection = ThresholdDirection.Above;
        public ThresholdDirection ThresholdDirection
        {
            get => _thresholdDirection;
            set
            {
                if (_thresholdDirection == value) return;
                _thresholdDirection = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAboveDirection));
                OnPropertyChanged(nameof(IsBelowDirection));
                PushStateToService();
                RecalculateColor();
            }
        }
        public bool IsAboveDirection
        {
            get => ThresholdDirection == ThresholdDirection.Above;
            set
            {
                if (value)
                {
                    IsThresholdEnabled = true; // checking a direction implies the user wants the threshold active
                    ThresholdDirection = ThresholdDirection.Above;
                }
                else
                {
                    // force the toggle back to checked; direction is radio-like, not a real off-state
                    OnPropertyChanged(nameof(IsAboveDirection));
                }
            }
        }
        public bool IsBelowDirection
        {
            get => ThresholdDirection == ThresholdDirection.Below;
            set
            {
                if (value)
                {
                    IsThresholdEnabled = true;
                    ThresholdDirection = ThresholdDirection.Below;
                }
                else
                {
                    OnPropertyChanged(nameof(IsBelowDirection));
                }
            }
        }
        private Windows.UI.Color _thresholdColor = Windows.UI.Color.FromArgb(255, 220, 50, 50);
        public Windows.UI.Color ThresholdColor
        {
            get => _thresholdColor;
            set
            {
                if (_thresholdColor == value) return;
                _thresholdColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThresholdColorBrush));
                PushStateToService();
                RecalculateColor();
            }
        }
        public SolidColorBrush ThresholdColorBrush
        {
            get
            {
                var c = ThresholdColor;
                const byte swatchAlpha = 200; // 255 = fully opaque
                return new SolidColorBrush(Windows.UI.Color.FromArgb(swatchAlpha, c.R, c.G, c.B));
            }
        }
        public Microsoft.UI.Xaml.Media.Brush AboveDirectionBrush => GetDirectionBrush(ThresholdDirection.Above);
        public Microsoft.UI.Xaml.Media.Brush BelowDirectionBrush => GetDirectionBrush(ThresholdDirection.Below);
        private Microsoft.UI.Xaml.Media.Brush GetDirectionBrush(ThresholdDirection buttonDirection)
        {
            bool isActive = ThresholdDirection == buttonDirection;
            string resourceKey = isActive ? "AccentFillColorDefaultBrush" : "ControlFillColorDefaultBrush";
            return (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[resourceKey];
        }
        private Brush _currentValueColor;
        public Brush CurrentValueColor
        {
            get => _currentValueColor;
            set { _currentValueColor = value; OnPropertyChanged(); }
        }

        // pushes the full state snapshot (threshold + Y-axis) to the shared service, so
        // MainWindow can pick up threshold changes and disk persistence stays up to date
        private void PushStateToService()
        {
            var state = SensorStateService.Instance.GetState(SensorId);
            state.Threshold = new SensorThreshold
            {
                IsEnabled = _isThresholdEnabled,
                Value = _manualThreshold,
                Direction = _thresholdDirection,
                Color = _thresholdColor
            };
            state.IsAutoScaled = _isAutoScaled;
            state.ManualYMax = _manualYMax;
            SensorStateService.Instance.SetState(SensorId, state);
        }

        // single visibility state for all control panels; toggled together, shown together
        private Visibility _controlPanelVisibility = Visibility.Collapsed;
        public Visibility ControlPanelVisibility
        {
            get => _controlPanelVisibility;
            set
            {
                if (_controlPanelVisibility != value)
                {
                    _controlPanelVisibility = value;
                    OnPropertyChanged();
                }
            }
        }


        // === event handlers ===

        private void OnThemeChanged(string newTheme)
        {
            RecalculateColor();
        }

        private void OnGraphColorChanged(bool useAccent, Windows.UI.Color customColor)
        {
            GraphColor = ResolveGraphColor(useAccent, customColor);
        }

        private void OnGraphDataPointsChanged(int newCount)
        {
            int currentCount = SensorData.Count;

            if (newCount > currentCount)
            {
                // the list got bigger -> add blank points (0.0) to the left (beginning of the list)
                int pointsToAdd = newCount - currentCount;
                for (int i = 0; i < pointsToAdd; i++)
                {
                    SensorData.Insert(0, 0.0);
                }
            }
            else if (newCount < currentCount)
            {
                // the list got smaller -> remove the oldest points on the left
                int pointsToRemove = currentCount - newCount;
                for (int i = 0; i < pointsToRemove; i++)
                {
                    SensorData.RemoveAt(0);
                }
            }
        }


        // === public methods ===

        // unsubscribes from SettingsService events; without this, disposed sensor rows would still react to
        // graph color / data point changes after being removed
        public void Cleanup()
        {
            SettingsService.Instance.GraphColorChanged -= OnGraphColorChanged;
            SettingsService.Instance.GraphDataPointsChanged -= OnGraphDataPointsChanged;
            SettingsService.Instance.ThemeChanged -= OnThemeChanged;
        }

        // data processing
        public void AddDataPoint(double newValue, string formattedValueText)
        {
            _currentRaw = newValue;

            // update the current value text
            CurrentValueText = formattedValueText;

            // shift the graph by one tick
            SensorData.RemoveAt(0);
            SensorData.Add(newValue);

            UpdateYMaxDisplay();
            RecalculateColor();
        }

        // user interaction
        // pane toggle button
        public void ToggleControlPanel()
        {
            ControlPanelVisibility = ControlPanelVisibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        // control buttons
        public void IncreaseYMax()
        {
            IsAutoScaled = false; // automatically turns off the auto button in the ui
            ManualYMax += _yMaxStep;
        }

        public void DecreaseYMax()
        {
            IsAutoScaled = false; // automatically turns off the auto button in the ui

            // preventing the y-axis from falling to 0 or into the negative range
            if (ManualYMax > _yMaxStep)
            {
                ManualYMax -= _yMaxStep;
            }
        }

        // threshold buttons
        public void IncreaseThreshold()
        {
            IsThresholdEnabled = true;  // auto-enable when the user adjusts the value
            ManualThreshold += _thresholdStep;
        }

        public void DecreaseThreshold()
        {
            IsThresholdEnabled = true;  // auto-enable when the user adjusts the value

            // preventing the threshold from falling to 0 or into the negative range
            if (ManualThreshold > _thresholdStep)
            {
                ManualThreshold -= _thresholdStep;
            }
        }


        // === private helpers ===

        // re-evaluates the current values color against this sensors own threshold config
        private void RecalculateColor()
        {
            if (!_isThresholdEnabled)
            {
                CurrentValueColor = DefaultTextColor.Resolve();
                return;
            }

            bool isBreached = _thresholdDirection == ThresholdDirection.Above
                ? _currentRaw > _manualThreshold
                : _currentRaw < _manualThreshold;

            CurrentValueColor = isBreached ? new SolidColorBrush(_thresholdColor) : DefaultTextColor.Resolve();
        }

        // calculates, what has to be displayed in the UI as the current max value
        private void UpdateYMaxDisplay()
        {
            if (IsAutoScaled)
            {
                // finds the highest point in the graph; the ?? 0 handles the case where the list is still empty
                double currentHighestPoint = SensorData.Max() ?? 0;
                ActualYMaxText = $"{currentHighestPoint:0.0}";
            }
            else
            {
                // if manual, we simply show the raw number
                ActualYMaxText = ManualYMax.ToString("0");
            }
        }

        // resolves the current accent-color setting to a concrete Color value
        private static Windows.UI.Color ResolveGraphColor(bool useAccent, Windows.UI.Color customColor)
        {
            if (useAccent)
            {
                return (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
            }
            return customColor;
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}