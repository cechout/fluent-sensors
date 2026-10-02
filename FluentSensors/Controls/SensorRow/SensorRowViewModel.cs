using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Persistence.Services;
using FluentSensors.Common.UI;
using FluentSensors.Common.Sensors;
using FluentSensors.Core.Lhm;
using FluentSensors.Controls.Threshold;


namespace FluentSensors.Controls.SensorRow
{
    public class SensorRowViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // the statistics
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
            SettingsService.Instance.DataUnitBasisChanged += OnDataUnitBasisChanged;
        }

        private void OnThemeChanged(string newTheme)
        {
            if (_dispatcherQueue != null)
                _dispatcherQueue.TryEnqueue(RecalculateColors);
            else
                RecalculateColors();
        }

        // the unit column moves only with a data unit switch, the four values follow on the next tick
        // also the one place that resets the threshold and y-axis values: every live sensor has exactly one row, hidden
        // or not, so this reaches closed windows too
        private void OnDataUnitBasisChanged()
        {
            if (_entry == null) return;

            string unit = SensorUnitFormatter.GetUnit(_entry.SensorType);
            if (unit == Unit) return; // the other data unit setting

            Unit = unit;
            SensorStateService.Instance.ResetUnitDependentValues(_entry.Id);
        }


        // === bindable properties ===

        // the hardware kind of the group (HardwareGroupViewModel.Kind); LhmSensorEntry has none, and a
        // pinned sensor needs its category
        public HardwareGroupKind HardwareKind { get; set; } = HardwareGroupKind.Other;

        // the backing sensor; Id, Name, SensorType and the live Value; set once in the initializer, after IsHidden, so
        // the first sync skips hidden rows
        private LhmSensorEntry _entry;
        public LhmSensorEntry Entry
        {
            get => _entry;
            set
            {
                if (_entry == value) return; // no double subscription
                _entry = value;

                Unit = SensorUnitFormatter.GetUnit(value.SensorType);
                OnPropertyChanged(nameof(Id));

                // the UI thread of this row, for the theme recolor
                _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

                InitializeThreshold();
                _entry.PropertyChanged += OnEntryPropertyChanged;

                // hidden and disabled sensors show no live values
                if (!IsHidden)
                {
                    UpdateValue(_entry.Value);
                }
            }
        }

        public string Id => _entry?.Id;
        public string Name => _entry?.Name ?? "Unknown Sensor";
        public string SensorType => _entry?.SensorType ?? "";
        public int SortOrder { get; set; } // creation order
        private string _unit = "";
        public string Unit
        {
            get => _unit;
            private set
            {
                if (_unit != value)
                {
                    _unit = value;
                    OnPropertyChanged();
                }
            }
        }

        // threshold, the shared editor; null until Entry is set (see InitializeThreshold)
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

        // formatted values
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

        // text colors
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

        // unsubscribes everything once the row is removed for good (not moved to the hidden list), or it keeps reacting
        public void Cleanup()
        {
            SettingsService.Instance.ThemeChanged -= OnThemeChanged;
            SettingsService.Instance.DataUnitBasisChanged -= OnDataUnitBasisChanged;
            if (_entry != null) _entry.PropertyChanged -= OnEntryPropertyChanged;
            Threshold?.Cleanup();
            if (Threshold != null) Threshold.PropertyChanged -= OnThresholdPropertyChanged;
        }


        // === private helpers ===

        private void InitializeThreshold()
        {
            if (_entry == null || Threshold != null) return;

            Threshold = new ThresholdEditorViewModel(_entry.Id, _entry.SensorType);
            Threshold.PropertyChanged += OnThresholdPropertyChanged;
            RecalculateColors();
        }

        // live value ticks from LhmHardwareTreeService
        private void OnEntryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LhmSensorEntry.Value)) return;

            // hidden and disabled sensors show no live values
            if (IsHidden) return;

            // already on the UI thread
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

        // one tick: min, max, avg and the formatted strings
        private void UpdateValue(double newValue)
        {
            if (newValue < _min) _min = newValue;
            if (newValue > _max) _max = newValue;

            _sum += newValue;
            _count++;
            _currentRaw = newValue;
            _avg = _sum / _count;

            // each value scales on its own, Min can read MHz while Max reads GHz
            CurrentValue = SensorUnitFormatter.Format(newValue, SensorType);
            MinimumValue = SensorUnitFormatter.Format(_min, SensorType);
            MaximumValue = SensorUnitFormatter.Format(_max, SensorType);
            AverageValue = SensorUnitFormatter.Format(_avg, SensorType);

            RecalculateColors();
        }

        private void RecalculateColors()
        {
            if (_count == 0) return; // no values yet

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
