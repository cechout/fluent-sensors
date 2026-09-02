using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using FluentSensors.Core;
using FluentSensors.Persistence.Services;
using FluentSensors.Common.UI;
using FluentSensors.Common.Sensors;
using FluentSensors.Controls.Threshold;


namespace FluentSensors.Controls.SensorGraph
{
    public class SensorGraphViewModel : INotifyPropertyChanged
    {
        // === fields ===

        private double _currentRaw;
        private readonly double _yMaxStep;
        private double? _timeSpanOverrideSeconds;


        // === constructor ===

        public SensorGraphViewModel(
            string sensorId,
            string sensorName,
            string sensorType,
            double? graphTimeSpanSecondsOverride = null,
            SensorGraphScope scope = SensorGraphScope.Widget)
        {
            SensorId = sensorId;
            SensorType = sensorType;
            SensorName = sensorName;
            Scope = scope;
            Unit = SensorUnitFormatter.GetUnit(sensorType);
            CurrentValueText = "-"; // placeholder text until we have the first value
            CurrentValueColor = DefaultTextColor.Resolve(FollowsSystemTheme);

            // when set, this instance owns a fixed time span independent of the scope GraphTimeSpanSeconds setting
            _timeSpanOverrideSeconds = graphTimeSpanSecondsOverride;
            int initialPointCount = CalculatePointCount(ResolveTimeSpanSeconds(), HardwareMonitorService.Instance.UpdateIntervalMs);

            // this raw data list will be plotted by LiveCharts
            // we use LINQ Enumerable.Repeat to fill the entire list with "0.0" values at startup
            SensorData = new ObservableCollection<double?>(Enumerable.Repeat<double?>(0.0, initialPointCount));

            if (Scope == SensorGraphScope.Taskbar)
            {
                GraphColor = ResolveGraphColor(SettingsService.Instance.TaskbarUseGraphAccentColor, SettingsService.Instance.TaskbarGraphCustomColor);
                _isCardBackgroundVisible = !SettingsService.Instance.TaskbarUseTransparentGraphBackground;
                SettingsService.Instance.TaskbarGraphColorChanged += OnGraphColorChanged;
                SettingsService.Instance.TaskbarGraphTimeSpanChanged += OnGraphTimeSpanChanged;
                SettingsService.Instance.TaskbarGraphBackgroundChanged += OnGraphBackgroundChanged;
            }
            else
            {
                GraphColor = ResolveGraphColor(SettingsService.Instance.UseGraphAccentColor, SettingsService.Instance.GraphCustomColor);
                SettingsService.Instance.GraphColorChanged += OnGraphColorChanged;
                SettingsService.Instance.GraphTimeSpanChanged += OnGraphTimeSpanChanged;
            }

            HardwareMonitorService.Instance.UpdateIntervalChanged += OnUpdateIntervalChanged;
            SettingsService.Instance.ThemeChanged += OnThemeChanged;

            // owns this sensors threshold config; shared logic/state lives there, this VM only reacts to it for coloring
            Threshold = new ThresholdEditorViewModel(sensorId, sensorType);
            Threshold.PropertyChanged += OnThresholdPropertyChanged;

            // per-sensor-type starting values for the y-axis; a clock sensor needs a much higher scale than a load percentage
            var profile = SensorTypeProfiles.GetProfile(sensorType);
            _yMaxStep = profile.YMaxStep;

            // restore this sensors Y-axis state for the current presentation scope
            var existingState = SensorStateService.Instance.GetState(SensorId);
            var yAxisState = existingState.GetYAxis(Scope);
            _isAutoScaled = yAxisState.IsAutoScaled;
            _manualYMax = yAxisState.ManualYMax ?? profile.YMaxDefault;

            UpdateYMaxDisplay();
        }


        // === bindable properties ===

        public SensorGraphScope Scope { get; }

        // the taskbar widget window follows the Windows theme rather than the app theme setting, so its graphs have to
        // resolve their text color the same way or they end up unreadable whenever the two disagree
        private bool FollowsSystemTheme => Scope == SensorGraphScope.Taskbar;

        // general
        public ObservableCollection<double?> SensorData { get; private set; }
        public string SensorId { get; }
        public string SensorType { get; }
        private string _sensorName = "not provided";
        public string SensorName
        {
            get => _sensorName;
            set { _sensorName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayNameWithUnit)); }
        }

        public string Unit { get; }

        public string DisplayNameWithUnit => string.IsNullOrEmpty(Unit) ? SensorName : $"{SensorName} ({Unit})";

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

        // taskbar widget graphs can drop their calculated card tint and go fully transparent
        // only the widget template binds this; the flyout renders these same instances with its own defaults,
        // so it keeps its themed card background either way
        private bool _isCardBackgroundVisible = true;
        public bool IsCardBackgroundVisible
        {
            get => _isCardBackgroundVisible;
            private set
            {
                if (_isCardBackgroundVisible == value) return;
                _isCardBackgroundVisible = value;
                OnPropertyChanged();
            }
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
            var yAxisState = state.GetYAxis(Scope);
            yAxisState.IsAutoScaled = _isAutoScaled;
            yAxisState.ManualYMax = _manualYMax;
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

        private void OnGraphBackgroundChanged(bool useTransparentBackground)
        {
            IsCardBackgroundVisible = !useTransparentBackground;
        }

        private void OnGraphTimeSpanChanged(double newTimeSpanSeconds)
        {
            // instances with a fixed override never resize with the global setting
            if (_timeSpanOverrideSeconds.HasValue) return;
            RecalculatePointCount();
        }

        // polling interval affects the point count regardless of whether this instance uses the global time span
        // or a fixed override
        private void OnUpdateIntervalChanged(int newIntervalMs)
        {
            RecalculatePointCount();
        }


        // === public methods ===

        // re-resolves the graph color against the current settings and the live SystemAccentColor
        // the constructor resolves it once, and SettingsService only reports the users own accent/custom switch;
        // a Windows accent change reaches this instance through nothing else
        public void RefreshGraphColor()
        {
            GraphColor = Scope == SensorGraphScope.Taskbar
                ? ResolveGraphColor(SettingsService.Instance.TaskbarUseGraphAccentColor, SettingsService.Instance.TaskbarGraphCustomColor)
                : ResolveGraphColor(SettingsService.Instance.UseGraphAccentColor, SettingsService.Instance.GraphCustomColor);
        }

        // unsubscribes from SettingsService events and the threshold editor; without this, disposed sensor rows would
        // still react to graph color / data point / threshold changes after being removed
        public void Cleanup()
        {
            if (Scope == SensorGraphScope.Taskbar)
            {
                SettingsService.Instance.TaskbarGraphColorChanged -= OnGraphColorChanged;
                SettingsService.Instance.TaskbarGraphTimeSpanChanged -= OnGraphTimeSpanChanged;
                SettingsService.Instance.TaskbarGraphBackgroundChanged -= OnGraphBackgroundChanged;
            }
            else
            {
                SettingsService.Instance.GraphColorChanged -= OnGraphColorChanged;
                SettingsService.Instance.GraphTimeSpanChanged -= OnGraphTimeSpanChanged;
            }

            HardwareMonitorService.Instance.UpdateIntervalChanged -= OnUpdateIntervalChanged;
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

        // wipes this graphs history back to empty; used when the Widget window is closed, so a hidden widget holds no
        // data at all (see WidgetViewModel.SetLiveDataActive)
        public void ClearHistory()
        {
            SensorData.Clear();
            CurrentValueText = "-"; // back to the placeholder until the next value
        }

        // refills this graph to a flat zero baseline at the current point count; used when a closed Widget window is
        // reopened, so it starts fresh instead of resuming the pre-close history
        // deliberately not called on minimize; a minimized widget keeps feeding data and preserves its history
        public void ResetToBaseline()
        {
            double effectiveSeconds = _timeSpanOverrideSeconds ?? SettingsService.Instance.GraphTimeSpanSeconds;
            int pointCount = CalculatePointCount(effectiveSeconds, HardwareMonitorService.Instance.UpdateIntervalMs);

            SensorData.Clear();
            for (int i = 0; i < pointCount; i++)
            {
                SensorData.Add(0.0);
            }

            CurrentValueText = "-"; // back to the placeholder until the first value after reopen
        }

        // applies view-specific configuration that intentionally does NOT persist to SensorStateService:
        // used by consumers like the Performance page that need this graphs time span / Y-axis behavior fixed and
        // decoupled from whatever is (or isnt) configured for this sensor elsewhere
        public void ApplyViewOverrides(double? graphTimeSpanSecondsOverride, bool? isAutoScaled, double? manualYMax)
        {
            if (graphTimeSpanSecondsOverride.HasValue && graphTimeSpanSecondsOverride.Value != _timeSpanOverrideSeconds)
            {
                _timeSpanOverrideSeconds = graphTimeSpanSecondsOverride; // also stops OnGraphTimeSpanChanged from resizing this instance later
                RecalculatePointCount();
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

        // recomputes the point count from whichever time span currently applies (override or global setting) plus
        // the current polling interval, and resizes to it
        private void RecalculatePointCount()
        {
            int newCount = CalculatePointCount(ResolveTimeSpanSeconds(), HardwareMonitorService.Instance.UpdateIntervalMs);
            ResizeSensorData(newCount);
        }

        // the time span this instance currently plots: its own fixed override if it has one, otherwise the setting
        // belonging to its scope
        // shared by the constructor and every later resize on purpose; resolving it separately in the two places is
        // what let taskbar graphs get rebuilt against the widget windows range instead of their own
        private double ResolveTimeSpanSeconds()
        {
            if (_timeSpanOverrideSeconds.HasValue) return _timeSpanOverrideSeconds.Value;

            return Scope == SensorGraphScope.Taskbar
                ? SettingsService.Instance.TaskbarGraphTimeSpanSeconds
                : SettingsService.Instance.GraphTimeSpanSeconds;
        }

        // how many points a graph needs to cover timeSpanSeconds at the given polling interval
        // e.g. 30s at a 500ms interval -> 60 points
        private static int CalculatePointCount(double timeSpanSeconds, int intervalMs)
        {
            return Math.Max(1, (int)Math.Round(timeSpanSeconds * 1000.0 / intervalMs));
        }

        // shared point-count resize logic
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
                : DefaultTextColor.Resolve(FollowsSystemTheme);
        }

        // calculates, what has to be displayed in the UI as the current max value
        private void UpdateYMaxDisplay()
        {
            if (IsAutoScaled)
            {
                // finds the highest point in the graph; the ?? 0 handles the case where the list is still empty
                double currentHighestPoint = SensorData.Max() ?? 0;
                var (scaledValue, _) = SensorUnitFormatter.Scale(currentHighestPoint, SensorType);
                ActualYMaxText = $"{scaledValue:0.0}";
            }
            else
            {
                // manual value, one decimal once Clock/SmallData crossed into GHz/GB, whole number otherwise exactly as before
                var (scaledValue, unit) = SensorUnitFormatter.Scale(ManualYMax, SensorType);
                ActualYMaxText = unit == SensorUnitFormatter.GetUnit(SensorType)
                    ? scaledValue.ToString("0")
                    : $"{scaledValue:0.0}";
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
