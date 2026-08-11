using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Persistence.Services;
using FluentSensors.Common.UI;
using FluentSensors.Common.Sensors;
using FluentSensors.Core.Lhm;


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

        // backing sensor:
        // source for Id/Name/SensorType and live Value; set once via object initializer must be set AFTER IsHidden, so
        // the initial sync below correctly skips hidden rows
        private LhmSensorEntry _entry;
        public LhmSensorEntry Entry
        {
            get => _entry;
            set
            {
                if (_entry == value) return; // set once; guards against double-subscribing
                _entry = value;

                Unit = SensorUnitFormatter.GetUnit(value.SensorType);
                OnPropertyChanged(nameof(Id));

                // captures the UI thread this row was created on, so live/theme updates can be marshalled back here safely
                _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

                InitializeThreshold();
                _entry.PropertyChanged += OnEntryPropertyChanged;

                // hidden and disabled sensors never show live values anywhere, so skip the initial sync entirely
                if (!IsHidden)
                {
                    UpdateValue(_entry.Value);
                }
            }
        }

        public string Id => _entry?.Id;
        public string Name => _entry?.Name ?? "Unknown Sensor";
        public string SensorType => _entry?.SensorType ?? "";
        public int SortOrder { get; set; } // original creation order
        public string Unit { get; private set; } = "";

        // threshold, owned by the shared editor; created once Entry is set (see InitializeThreshold), null before that
        public ThresholdEditorViewModel Threshold { get; private set; }

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

                    // persist immediately so the checkbox state survives an app restart; Entry is set before IsSelected
                    // in the object initializer (SensorsViewModel), so Id is always available here
                    if (Id != null)
                    {
                        SensorStateService.Instance.SetSelected(Id, value);
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

        // unsubscribes from SettingsService, the backing entry, and the threshold editor; must be called once this
        // row is permanently removed (not just moved to the hidden list), or it keeps reacting to value/theme/threshold
        // changes after disposal
        public void Cleanup()
        {
            SettingsService.Instance.ThemeChanged -= OnThemeChanged;
            if (_entry != null) _entry.PropertyChanged -= OnEntryPropertyChanged;
            Threshold?.Cleanup();
            if (Threshold != null) Threshold.PropertyChanged -= OnThresholdPropertyChanged;
        }


        // === private helpers ===

        // creates this rows threshold editor once Entry is known
        private void InitializeThreshold()
        {
            if (_entry == null || Threshold != null) return;

            Threshold = new ThresholdEditorViewModel(_entry.Id, _entry.SensorType);
            Threshold.PropertyChanged += OnThresholdPropertyChanged;
            RecalculateColors();
        }

        // reacts to live value ticks pushed by LhmHardwareTreeService via the backing entry
        private void OnEntryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LhmSensorEntry.Value)) return;

            // hidden and disabled sensors never show live values anywhere, so skip updating them entirely
            if (IsHidden) return;

            // no dispatch needed: LhmHardwareTreeService already raises this on the UI thread
            UpdateValue(_entry.Value);
        }

        private void OnThresholdPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ThresholdEditorViewModel.IsEnabled) ||
                e.PropertyName == nameof(ThresholdEditorViewModel.Value) ||
                e.PropertyName == nameof(ThresholdEditorViewModel.Direction) ||
                e.PropertyName == nameof(ThresholdEditorViewModel.Color))
            {
                RecalculateColors();
            }
        }

        // applies one new value tick: updates min/max/avg and the formatted display strings
        private void UpdateValue(double newValue)
        {
            if (newValue < _min) _min = newValue;
            if (newValue > _max) _max = newValue;

            _sum += newValue;
            _count++;
            _currentRaw = newValue;
            _avg = _sum / _count;

            // each value picks its own scale independently, so Min can still read MHz while Max already switched to GHz
            CurrentValue = SensorUnitFormatter.Format(newValue, SensorType);
            MinimumValue = SensorUnitFormatter.Format(_min, SensorType);
            MaximumValue = SensorUnitFormatter.Format(_max, SensorType);
            AverageValue = SensorUnitFormatter.Format(_avg, SensorType);

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
            if (Threshold == null || !Threshold.IsBreached(value))
                return DefaultTextColor.Resolve();

            return new SolidColorBrush(Threshold.Color);
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}