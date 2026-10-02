using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using FluentSensors.Core;
using FluentSensors.Persistence.Models;
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
        private double? _timeSpanOverrideSeconds;

        // both in the displayed base unit, see SensorTypeProfile
        private readonly double _yMaxStep;
        private readonly double _yMaxDefault;

        // a view override owns ManualYMax (see ApplyViewOverrides); a data unit switch leaves it alone
        private bool _hasManualYMaxOverride;

        // the UI thread of this graph, for a y-axis change from another window (see OnSensorStateChanged)
        private readonly DispatcherQueue _dispatcherQueue;

        // snapshot (see SetFrozen); the values that arrive meanwhile, at most one graph full, and the latest text
        private bool _isFrozen;
        private readonly Queue<double> _pendingValues = new();
        private string? _pendingValueText;


        // === constructor ===

        public SensorGraphViewModel(
            string sensorId,
            string sensorName,
            string sensorType,
            double? graphTimeSpanSecondsOverride = null,
            SensorGraphScope scope = SensorGraphScope.Widget,
            HardwareGroupKind hardwareKind = HardwareGroupKind.Other)
        {
            SensorId = sensorId;
            SensorType = sensorType;
            SensorName = sensorName;
            Scope = scope;
            HardwareKind = hardwareKind;
            Unit = SensorUnitFormatter.GetUnit(sensorType);
            CurrentValueText = "-"; // until the first value
            CurrentValueColor = DefaultTextColor.Resolve();

            // a fixed time span, independent of the scope setting
            _timeSpanOverrideSeconds = graphTimeSpanSecondsOverride;
            int initialPointCount = CalculatePointCount(ResolveTimeSpanSeconds(), HardwareMonitorService.Instance.UpdateIntervalMs);

            // the plotted points, a flat zero baseline at start
            SensorData = new ObservableCollection<double?>(Enumerable.Repeat<double?>(0.0, initialPointCount));

            if (IsTaskbarScope)
            {
                GraphColor = ResolveGraphColor(SettingsService.Instance.TaskbarGraphColorSource, SettingsService.Instance.TaskbarGraphCustomColor);
                _isCardBackgroundVisible = !SettingsService.Instance.TaskbarUseTransparentGraphBackground;
                SettingsService.Instance.TaskbarGraphColorChanged += OnGraphColorChanged;
                SettingsService.Instance.TaskbarGraphBackgroundChanged += OnGraphBackgroundChanged;

                // the taskbar and its flyout are two graphs on one y-axis state
                SensorStateService.Instance.StateChanged += OnSensorStateChanged;
            }
            else
            {
                GraphColor = ResolveGraphColor(SettingsService.Instance.GraphColorSource, SettingsService.Instance.GraphCustomColor);
                SettingsService.Instance.GraphColorChanged += OnGraphColorChanged;
            }

            switch (Scope)
            {
                case SensorGraphScope.Taskbar: SettingsService.Instance.TaskbarGraphTimeSpanChanged += OnGraphTimeSpanChanged; break;
                case SensorGraphScope.TaskbarFlyout: SettingsService.Instance.TaskbarFlyoutGraphTimeSpanChanged += OnGraphTimeSpanChanged; break;
                default: SettingsService.Instance.GraphTimeSpanChanged += OnGraphTimeSpanChanged; break;
            }

            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            // line style and fill fade are global, the same for every scope
            GraphLineStyle = SettingsService.Instance.GraphLineStyle;
            GraphFillFade = SettingsService.Instance.GraphFillFade;

            HardwareMonitorService.Instance.UpdateIntervalChanged += OnUpdateIntervalChanged;
            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            SettingsService.Instance.GraphLineStyleChanged += OnGraphLineStyleChanged;
            SettingsService.Instance.GraphFillFadeChanged += OnGraphFillFadeChanged;
            SettingsService.Instance.DataUnitBasisChanged += OnDataUnitBasisChanged;

            // the threshold config of this sensor; this view model only colors by it
            Threshold = new ThresholdEditorViewModel(sensorId, sensorType);
            Threshold.PropertyChanged += OnThresholdPropertyChanged;

            // y-axis start values per sensor type; (a clock needs a far higher scale than a load)
            var profile = SensorTypeProfiles.GetProfile(sensorType);
            _yMaxStep = profile.YMaxStep;
            _yMaxDefault = profile.YMaxDefault;

            // the saved Y-axis state of this scope
            var existingState = SensorStateService.Instance.GetState(SensorId);
            var yAxisState = existingState.GetYAxis(Scope);
            _isAutoScaled = yAxisState.IsAutoScaled;
            _manualYMax = yAxisState.ManualYMax ?? SensorUnitFormatter.ToRawValue(_yMaxDefault, sensorType);

            UpdateYMaxDisplay();
        }


        // === bindable properties ===

        public SensorGraphScope Scope { get; }

        // the taskbar widget or its flyout; both follow the taskbar graph settings
        private bool IsTaskbarScope => Scope is SensorGraphScope.Taskbar or SensorGraphScope.TaskbarFlyout;

        // only for the graph colour (see ResolveGraphColor); Other resolves like hardware colours off
        public HardwareGroupKind HardwareKind { get; }

        // CurrentValueColor carries a threshold color, not the default; for consumers resolving the default against
        // their own theme (see SensorPanelControl.GetCurrentValueColorOrDefault)
        // no notification of its own, the CurrentValueColor one carries it
        public bool IsThresholdColorActive { get; private set; }

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

        private string _unit = "";
        public string Unit
        {
            get => _unit;
            private set
            {
                if (_unit == value) return;
                _unit = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayNameWithUnit));
            }
        }

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

        // stepline or smooth; mirrors the global switch like GraphColor
        private GraphLineStyle _graphLineStyle;
        public GraphLineStyle GraphLineStyle
        {
            get => _graphLineStyle;
            private set { _graphLineStyle = value; OnPropertyChanged(); }
        }

        // flat fill or a fade towards the bottom; global, like GraphLineStyle
        private bool _graphFillFade;
        public bool GraphFillFade
        {
            get => _graphFillFade;
            private set { _graphFillFade = value; OnPropertyChanged(); }
        }

        // taskbar widget graphs can go fully transparent; only the widget template binds
        // this, the flyout keeps its card
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

        // threshold; the shared editor, for bindings like Threshold.Value
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

        // the Y-axis part of the state only; Threshold persists its own
        private void PushYAxisStateToService()
        {
            var state = SensorStateService.Instance.GetState(SensorId);
            var yAxisState = state.GetYAxis(Scope);
            yAxisState.IsAutoScaled = _isAutoScaled;
            yAxisState.ManualYMax = _manualYMax;
            SensorStateService.Instance.SetState(SensorId, state);
        }

        // one state for all control panels
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

        // the value color follows the threshold
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

        private void OnGraphColorChanged(GraphColorSource source, Windows.UI.Color customColor)
        {
            GraphColor = ResolveGraphColor(source, customColor);
        }

        private void OnGraphLineStyleChanged(GraphLineStyle style)
        {
            GraphLineStyle = style;
        }

        private void OnGraphFillFadeChanged(bool fillFade)
        {
            GraphFillFade = fillFade;
        }

        // only the title unit follows right away, the next data point rebuilds the rest
        // ManualYMax goes back to its round default in the new unit (the sensors row resets the persisted copy for
        // every scope); a view override is a real value like the total memory and stays
        private void OnDataUnitBasisChanged()
        {
            string unit = SensorUnitFormatter.GetUnit(SensorType);
            if (unit == Unit) return; // the other data unit setting

            Unit = unit;

            if (!_hasManualYMaxOverride)
            {
                _manualYMax = SensorUnitFormatter.ToRawValue(_yMaxDefault, SensorType);
                OnPropertyChanged(nameof(ManualYMax));
                UpdateYMaxDisplay();
            }
        }

        private void OnGraphBackgroundChanged(bool useTransparentBackground)
        {
            IsCardBackgroundVisible = !useTransparentBackground;
        }

        private void OnGraphTimeSpanChanged(double newTimeSpanSeconds)
        {
            // a fixed override never follows the setting
            if (_timeSpanOverrideSeconds.HasValue) return;
            RecalculatePointCount();
        }

        // the interval changes the point count, override or not
        private void OnUpdateIntervalChanged(int newIntervalMs)
        {
            RecalculatePointCount();
        }

        // the other graph of the taskbar pair moved the shared y-axis; taken over without pushing it back
        private void OnSensorStateChanged(string sensorId, SensorState state)
        {
            if (sensorId != SensorId) return;

            void Apply()
            {
                var yAxisState = state.GetYAxis(Scope);
                double manualYMax = yAxisState.ManualYMax ?? SensorUnitFormatter.ToRawValue(_yMaxDefault, SensorType);
                if (_isAutoScaled == yAxisState.IsAutoScaled && _manualYMax == manualYMax) return;

                _isAutoScaled = yAxisState.IsAutoScaled;
                _manualYMax = manualYMax;
                OnPropertyChanged(nameof(IsAutoScaled));
                OnPropertyChanged(nameof(ManualYMax));
                UpdateYMaxDisplay();
            }

            if (_dispatcherQueue != null) _dispatcherQueue.TryEnqueue(Apply);
            else Apply();
        }


        // === public methods ===

        // re-resolves the graph color against the live SystemAccentColor; the only way a Windows accent
        // change reaches this instance
        public void RefreshGraphColor()
        {
            GraphColor = IsTaskbarScope
                ? ResolveGraphColor(SettingsService.Instance.TaskbarGraphColorSource, SettingsService.Instance.TaskbarGraphCustomColor)
                : ResolveGraphColor(SettingsService.Instance.GraphColorSource, SettingsService.Instance.GraphCustomColor);
        }

        // unsubscribes everything, or a removed row keeps reacting
        public void Cleanup()
        {
            if (IsTaskbarScope)
            {
                SettingsService.Instance.TaskbarGraphColorChanged -= OnGraphColorChanged;
                SettingsService.Instance.TaskbarGraphBackgroundChanged -= OnGraphBackgroundChanged;
                SensorStateService.Instance.StateChanged -= OnSensorStateChanged;
            }
            else
            {
                SettingsService.Instance.GraphColorChanged -= OnGraphColorChanged;
            }

            SettingsService.Instance.TaskbarGraphTimeSpanChanged -= OnGraphTimeSpanChanged;
            SettingsService.Instance.TaskbarFlyoutGraphTimeSpanChanged -= OnGraphTimeSpanChanged;
            SettingsService.Instance.GraphTimeSpanChanged -= OnGraphTimeSpanChanged;

            HardwareMonitorService.Instance.UpdateIntervalChanged -= OnUpdateIntervalChanged;
            SettingsService.Instance.ThemeChanged -= OnThemeChanged;
            SettingsService.Instance.GraphLineStyleChanged -= OnGraphLineStyleChanged;
            SettingsService.Instance.GraphFillFadeChanged -= OnGraphFillFadeChanged;
            SettingsService.Instance.DataUnitBasisChanged -= OnDataUnitBasisChanged;
            Threshold.PropertyChanged -= OnThresholdPropertyChanged;
            Threshold.Cleanup();
        }

        public void AddDataPoint(double newValue, string formattedValueText)
        {
            if (_isFrozen)
            {
                _pendingValues.Enqueue(newValue);
                if (_pendingValues.Count > SensorData.Count) _pendingValues.Dequeue();
                _pendingValueText = formattedValueText;
                return;
            }

            _currentRaw = newValue;

            CurrentValueText = formattedValueText;

            // shifts the graph one tick
            SensorData.RemoveAt(0);
            SensorData.Add(newValue);

            UpdateYMaxDisplay();
            RecalculateColor();
        }

        // empties the history when the widget closes, a hidden widget holds no data (see
        // WidgetViewModel.SetLiveDataActive)
        public void ClearHistory()
        {
            SensorData.Clear();
            CurrentValueText = "-";
        }

        // a flat zero baseline when a closed widget reopens; (not on minimize, a minimized widget keeps its history)
        public void ResetToBaseline()
        {
            int pointCount = CalculatePointCount(ResolveTimeSpanSeconds(), HardwareMonitorService.Instance.UpdateIntervalMs);

            SensorData.Clear();
            for (int i = 0; i < pointCount; i++)
            {
                SensorData.Add(0.0);
            }

            CurrentValueText = "-";
        }

        // snapshot: frozen, line, value and y-axis stay where they are while the values that arrive are collected;
        // thawed, the graph takes them over and jumps to now with the history of the pause
        public void SetFrozen(bool frozen)
        {
            if (_isFrozen == frozen) return;
            _isFrozen = frozen;

            if (frozen || _pendingValues.Count == 0) return;

            // one new collection, so one repaint instead of one per value
            var values = SensorData.Concat(_pendingValues.Select(v => (double?)v)).ToList();
            SensorData = new ObservableCollection<double?>(values.Skip(values.Count - SensorData.Count));
            OnPropertyChanged(nameof(SensorData));

            _currentRaw = _pendingValues.Last();
            CurrentValueText = _pendingValueText ?? CurrentValueText;
            _pendingValues.Clear();
            _pendingValueText = null;

            UpdateYMaxDisplay();
            RecalculateColor();
        }

        // view-specific time span and Y-axis, never persisted (the performance page fixes them per view)
        public void ApplyViewOverrides(double? graphTimeSpanSecondsOverride, bool? isAutoScaled, double? manualYMax)
        {
            if (graphTimeSpanSecondsOverride.HasValue && graphTimeSpanSecondsOverride.Value != _timeSpanOverrideSeconds)
            {
                _timeSpanOverrideSeconds = graphTimeSpanSecondsOverride; // the setting no longer resizes it
                RecalculatePointCount();
            }

            if (isAutoScaled.HasValue && _isAutoScaled != isAutoScaled.Value)
            {
                _isAutoScaled = isAutoScaled.Value;
                OnPropertyChanged(nameof(IsAutoScaled));
            }

            if (manualYMax.HasValue) _hasManualYMaxOverride = true;

            if (manualYMax.HasValue && _manualYMax != manualYMax.Value)
            {
                _manualYMax = manualYMax.Value;
                OnPropertyChanged(nameof(ManualYMax));
            }

            UpdateYMaxDisplay();
        }

        // user interaction
        public void ToggleControlPanel()
        {
            ControlPanelVisibility = ControlPanelVisibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        public void IncreaseYMax()
        {
            IsAutoScaled = false; // turns the auto button off
            ManualYMax += SensorUnitFormatter.ToRawValue(_yMaxStep, SensorType);
        }

        public void DecreaseYMax()
        {
            IsAutoScaled = false; // turns the auto button off

            // never down to 0 or below
            double step = SensorUnitFormatter.ToRawValue(_yMaxStep, SensorType);
            if (ManualYMax > step)
            {
                ManualYMax -= step;
            }
        }


        // === private helpers ===

        // resizes to the point count of the current time span and interval
        private void RecalculatePointCount()
        {
            int newCount = CalculatePointCount(ResolveTimeSpanSeconds(), HardwareMonitorService.Instance.UpdateIntervalMs);
            ResizeSensorData(newCount);
        }

        // the override, otherwise the setting of this scope; (one resolver for the constructor and every resize, so a
        // taskbar graph never takes the widget range)
        private double ResolveTimeSpanSeconds()
        {
            if (_timeSpanOverrideSeconds.HasValue) return _timeSpanOverrideSeconds.Value;

            return Scope switch
            {
                SensorGraphScope.Taskbar => SettingsService.Instance.TaskbarGraphTimeSpanSeconds,
                SensorGraphScope.TaskbarFlyout => SettingsService.Instance.TaskbarFlyoutGraphTimeSpanSeconds,
                _ => SettingsService.Instance.GraphTimeSpanSeconds
            };
        }

        // points to cover timeSpanSeconds at the interval; 30s at 500ms = 60
        private static int CalculatePointCount(double timeSpanSeconds, int intervalMs)
        {
            return Math.Max(1, (int)Math.Round(timeSpanSeconds * 1000.0 / intervalMs));
        }

        private void ResizeSensorData(int newCount)
        {
            int currentCount = SensorData.Count;

            if (newCount > currentCount)
            {
                // bigger: zero points on the left
                int pointsToAdd = newCount - currentCount;
                for (int i = 0; i < pointsToAdd; i++)
                {
                    SensorData.Insert(0, 0.0);
                }
            }
            else if (newCount < currentCount)
            {
                // smaller: the oldest points go
                int pointsToRemove = currentCount - newCount;
                for (int i = 0; i < pointsToRemove; i++)
                {
                    SensorData.RemoveAt(0);
                }
            }
        }

        // the value color against the threshold
        private void RecalculateColor()
        {
            IsThresholdColorActive = Threshold.IsBreached(_currentRaw);

            CurrentValueColor = IsThresholdColorActive
                ? new SolidColorBrush(Threshold.Color)
                : DefaultTextColor.Resolve();
        }

        // the shown y-axis maximum
        private void UpdateYMaxDisplay()
        {
            if (IsAutoScaled)
            {
                // the highest point, 0 while empty
                double currentHighestPoint = SensorData.Max() ?? 0;
                var (scaledValue, _) = SensorUnitFormatter.Scale(currentHighestPoint, SensorType);
                ActualYMaxText = $"{scaledValue:0.0}";
            }
            else
            {
                // manual; one decimal once Clock or SmallData scaled to GHz or GB, whole otherwise
                var (scaledValue, unit) = SensorUnitFormatter.Scale(ManualYMax, SensorType);
                ActualYMaxText = unit == SensorUnitFormatter.GetUnit(SensorType)
                    ? scaledValue.ToString("0")
                    : $"{scaledValue:0.0}";
            }
        }

        // the color source of this surface (widget and taskbar can differ) to a color; Hardware falls back to the
        // accent without a category (the performance page passes a GraphColorOverride instead)
        private Windows.UI.Color ResolveGraphColor(GraphColorSource source, Windows.UI.Color customColor)
        {
            if (source == GraphColorSource.Hardware && HardwareKind != HardwareGroupKind.Other)
            {
                return HardwareGroupInfo.GetProfile(HardwareKind).Color;
            }

            if (source == GraphColorSource.Custom)
            {
                return customColor;
            }

            return (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
