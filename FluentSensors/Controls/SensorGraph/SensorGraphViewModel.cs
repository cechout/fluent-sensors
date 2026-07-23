using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using FluentSensors.Common;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Controls.SensorGraph
{
    public class SensorGraphViewModel : INotifyPropertyChanged
    {
        // === fields ===

        private double _currentRaw;
        private readonly double _yMaxStep;
        private int? _dataPointsOverride;


        // === constructor ===

        public SensorGraphViewModel(string sensorId, string sensorName, string sensorType, int? dataPointsOverride = null)
        {
            SensorId = sensorId;
            SensorName = sensorName;
            CurrentValueText = "-"; // placeholder text until we have the first value
            CurrentValueColor = DefaultTextColor.Resolve();

            // when set, this instance owns a fixed graph data point count independent of the global GraphDataPoints setting
            _dataPointsOverride = dataPointsOverride;
            int initialPointCount = dataPointsOverride ?? SettingsService.Instance.GraphDataPoints;

            // this raw data list will be plotted by LiveCharts
            // we use LINQ Enumerable.Repeat to fill the entire list with "0.0" values at startup
            SensorData = new ObservableCollection<double?>(Enumerable.Repeat<double?>(0.0, initialPointCount));

            GraphColor = ResolveGraphColor(SettingsService.Instance.UseGraphAccentColor, SettingsService.Instance.GraphCustomColor);
            SettingsService.Instance.GraphColorChanged += OnGraphColorChanged;
            SettingsService.Instance.GraphDataPointsChanged += OnGraphDataPointsChanged;
            SettingsService.Instance.ThemeChanged += OnThemeChanged;

            // owns this sensors threshold config; shared logic/state lives there, this VM only reacts to it for coloring
            Threshold = new ThresholdEditorViewModel(sensorId, sensorType);
            Threshold.PropertyChanged += OnThresholdPropertyChanged;

            // per-sensor-type starting values for the y-axis; a clock sensor needs a much higher scale than a load percentage
            var profile = SensorTypeProfiles.GetProfile(sensorType);
            _yMaxStep = profile.YMaxStep;

            // restore this sensors Y-axis state if it was already configured before (e.g. previously pinned, or loaded
            // from disk at startup); a null ManualYMax means the user never touched it yet, so we fall back to this
            // sensor types default instead of a generic one
            var existingState = SensorStateService.Instance.GetState(SensorId);
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

        // threshold: owned by the shared editor, exposed so views can bind e.g. Threshold.Value, Threshold.IsEnabled
        public ThresholdEditorViewModel Threshold { get; }

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
                    PushYAxisStateToService();
                }
            }
        }
        private double _manualYMax = 100; 
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
                    PushYAxisStateToService();
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

        private Brush _currentValueColor;
        public Brush CurrentValueColor
        {
            get => _currentValueColor;
            set { _currentValueColor = value; OnPropertyChanged(); }
        }

        // pushes only the Y-axis part of the state snapshot; Threshold manages and persists its own slice independently
        private void PushYAxisStateToService()
        {
            var state = SensorStateService.Instance.GetState(SensorId);
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

        // the current values color depends on the threshold, so any relevant change there needs a recolor
        private void OnThresholdPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ThresholdEditorViewModel.IsEnabled) ||
                e.PropertyName == nameof(ThresholdEditorViewModel.Value) ||
                e.PropertyName == nameof(ThresholdEditorViewModel.Direction) ||
                e.PropertyName == nameof(ThresholdEditorViewModel.Color))
            {
                RecalculateColor();
            }
        }

        private void OnGraphColorChanged(bool useAccent, Windows.UI.Color customColor)
        {
            GraphColor = ResolveGraphColor(useAccent, customColor);
        }

        private void OnGraphDataPointsChanged(int newCount)
        {
            // instances with a fixed override never resize with the global setting
            if (_dataPointsOverride.HasValue) return;
            ResizeSensorData(newCount);
        }


        // === public methods ===

        // unsubscribes from SettingsService events and the threshold editor; without this, disposed sensor rows would
        // still react to graph color / data point / threshold changes after being removed
        public void Cleanup()
        {
            SettingsService.Instance.GraphColorChanged -= OnGraphColorChanged;
            SettingsService.Instance.GraphDataPointsChanged -= OnGraphDataPointsChanged;
            SettingsService.Instance.ThemeChanged -= OnThemeChanged;
            Threshold.PropertyChanged -= OnThresholdPropertyChanged;
            Threshold.Cleanup();
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

        // applies view-specific configuration that intentionally does NOT persist to SensorStateService:
        // used by consumers like the Performance page that need this graphs data point count / Y-axis behavior fixed and
        // decoupled from whatever is (or isnt) configured for this sensor elsewhere (e.g. pinned in
        // the Widget)
        // passing null for any parameter leaves that aspect on its normal (persisted / globally
        // configured) behavior
        public void ApplyViewOverrides(int? dataPoints, bool? isAutoScaled, double? manualYMax)
        {
            if (dataPoints.HasValue && dataPoints.Value != SensorData.Count)
            {
                _dataPointsOverride = dataPoints; // also stops OnGraphDataPointsChanged from resizing this instance later
                ResizeSensorData(dataPoints.Value);
            }

            if (isAutoScaled.HasValue && _isAutoScaled != isAutoScaled.Value)
            {
                _isAutoScaled = isAutoScaled.Value;
                OnPropertyChanged(nameof(IsAutoScaled));
            }

            if (manualYMax.HasValue && _manualYMax != manualYMax.Value)
            {
                _manualYMax = manualYMax.Value;
                OnPropertyChanged(nameof(ManualYMax));
            }

            UpdateYMaxDisplay();
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


        // === private helpers ===

        // shared point-count resize logic, used both when the global GraphDataPoints setting changes (see
        // OnGraphDataPointsChanged) and when a per-instance override is applied from the view (see ApplyViewOverrides)
        private void ResizeSensorData(int newCount)
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

        // re-evaluates the current values color against this sensors own threshold config
        private void RecalculateColor()
        {
            CurrentValueColor = Threshold.IsBreached(_currentRaw)
                ? new SolidColorBrush(Threshold.Color)
                : DefaultTextColor.Resolve();
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