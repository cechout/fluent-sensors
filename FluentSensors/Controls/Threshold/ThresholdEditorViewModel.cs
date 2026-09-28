using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Persistence.Models;
using FluentSensors.Persistence.Services;
using FluentSensors.Common.Sensors;


namespace FluentSensors.Controls.Threshold
{
    // owns one sensors threshold configuration (enabled, value, direction, color) and keeps it in sync with
    // SensorStateService, shared between SensorRowControl (SensorsPage) and SensorGraphControl (WidgetWindow) so editing
    // the threshold in either place immediately reflects in the other
    public class ThresholdEditorViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // both in the displayed base unit, see SensorTypeProfile
        private readonly double _thresholdStep;
        private readonly double _thresholdDefault;

        // true while the value is still the untouched per-type default, which then follows a data unit switch
        private bool _isDefaultValue;

        private readonly DispatcherQueue _dispatcherQueue;


        // === constructor ===

        public ThresholdEditorViewModel(string sensorId, string sensorType)
        {
            SensorId = sensorId;
            SensorType = sensorType;

            // per-sensor-type step size, a clock sensor needs a much bigger step than a load percentage
            var profile = SensorTypeProfiles.GetProfile(sensorType);
            _thresholdStep = profile.ThresholdStep;
            _thresholdDefault = profile.ThresholdDefault;

            // restore this sensors threshold if it was already configured before; a null Value means the user never
            // touched it yet, so we fall back to this sensor types default instead of a generic one
            var existingThreshold = SensorStateService.Instance.GetState(sensorId).Threshold;
            _isEnabled = existingThreshold.IsEnabled;
            _isDefaultValue = existingThreshold.Value == null;
            _value = existingThreshold.Value ?? SensorUnitFormatter.ToRawValue(_thresholdDefault, sensorType);
            _direction = existingThreshold.Direction;
            _color = existingThreshold.Color;

            // captures the UI thread this editor was created on, so external threshold updates (from the other window)
            // can be marshalled back here safely
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            SensorStateService.Instance.StateChanged += OnStateChanged;
            SettingsService.Instance.DataUnitBasisChanged += OnDataUnitBasisChanged;
        }


        // === bindable properties ===

        public string SensorId { get; }
        public string SensorType { get; }

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveValue));
                PushStateToService();
            }
        }

        private double _value;
        public double Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveValue));
                PushStateToService();
            }
        }

        // null when disabled, drives the Graphs section visibility 
        public double? EffectiveValue => IsEnabled ? _value : (double?)null;

        private ThresholdDirection _direction = ThresholdDirection.Above;
        public ThresholdDirection Direction
        {
            get => _direction;
            set
            {
                if (_direction == value) return;
                _direction = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAboveDirection));
                OnPropertyChanged(nameof(IsBelowDirection));
                PushStateToService();
            }
        }
        public bool IsAboveDirection
        {
            get => Direction == ThresholdDirection.Above;
            set
            {
                if (!value)
                {
                    // force the toggle back to checked; direction is radio-like, not a real off-state
                    OnPropertyChanged(nameof(IsAboveDirection));
                    return;
                }

                // guard: TwoWay x:Bind re-syncs IsChecked (and therefore calls this setter again) any time
                // this controls bindings get re-evaluated, e.g. after an Unloaded/Loaded cycle
                // Not just on a real user click; without this check, a resync that merely re-confirms the current
                // direction would still unconditionally re-enable the threshold below
                if (Direction == ThresholdDirection.Above) return;

                IsEnabled = true;
                Direction = ThresholdDirection.Above;
            }
        }
        public bool IsBelowDirection
        {
            get => Direction == ThresholdDirection.Below;
            set
            {
                if (!value)
                {
                    OnPropertyChanged(nameof(IsBelowDirection));
                    return;
                }

                // guard
                if (Direction == ThresholdDirection.Below) return;

                IsEnabled = true;
                Direction = ThresholdDirection.Below;
            }
        }

        private Windows.UI.Color _color = Windows.UI.Color.FromArgb(255, 220, 50, 50);
        public Windows.UI.Color Color
        {
            get => _color;
            set
            {
                if (_color == value) return;
                _color = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColorBrush));
                PushStateToService();
            }
        }
        public SolidColorBrush ColorBrush
        {
            get
            {
                const byte swatchAlpha = 200; // 255 = fully opaque
                return new SolidColorBrush(Windows.UI.Color.FromArgb(swatchAlpha, Color.R, Color.G, Color.B));
            }
        }

        // kept for parity with the pre-split ViewModel; not currently bound anywhere
        public Brush AboveDirectionBrush => GetDirectionBrush(ThresholdDirection.Above);
        public Brush BelowDirectionBrush => GetDirectionBrush(ThresholdDirection.Below);
        private Brush GetDirectionBrush(ThresholdDirection buttonDirection)
        {
            bool isActive = Direction == buttonDirection;
            string resourceKey = isActive ? "AccentFillColorDefaultBrush" : "ControlFillColorDefaultBrush";
            return (Brush)Application.Current.Resources[resourceKey];
        }


        // === public methods ===

        // increase/decrease buttons
        public void Increase()
        {
            IsEnabled = true; // auto-enable when the user adjusts the value
            Value += SensorUnitFormatter.ToRawValue(_thresholdStep, SensorType);
        }

        public void Decrease()
        {
            IsEnabled = true;

            // preventing the threshold from falling to 0 or into the negative range
            double step = SensorUnitFormatter.ToRawValue(_thresholdStep, SensorType);
            if (Value > step)
            {
                Value -= step;
            }
        }

        // shared breach check, used by both SensorRowViewModel (text color) and SensorGraphViewModel (current value color)
        public bool IsBreached(double value)
        {
            if (!IsEnabled) return false;

            return Direction == ThresholdDirection.Above
                ? value > Value
                : value < Value;
        }

        // unsubscribes from SensorStateService; must be called by the owning ViewModels own Cleanup, or this editor
        // keeps reacting to state changes after its row/graph has been disposed
        public void Cleanup()
        {
            SensorStateService.Instance.StateChanged -= OnStateChanged;
            SettingsService.Instance.DataUnitBasisChanged -= OnDataUnitBasisChanged;
        }


        // === private helpers ===

        // an untouched default is a round number in the displayed unit, so it moves along with a data unit switch;
        // a value the user already set stays exactly where it is
        private void OnDataUnitBasisChanged()
        {
            if (!_isDefaultValue) return;

            _value = SensorUnitFormatter.ToRawValue(_thresholdDefault, SensorType);
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(EffectiveValue));
        }

        private void PushStateToService()
        {
            _isDefaultValue = false; // from here on the value is persisted, so it belongs to the user

            var state = SensorStateService.Instance.GetState(SensorId);
            state.Threshold = new SensorThreshold
            {
                IsEnabled = _isEnabled,
                Value = _value,
                Direction = _direction,
                Color = _color
            };
            SensorStateService.Instance.SetState(SensorId, state);
        }

        // reacts to threshold changes made anywhere else (the other window editing the same sensor); applies the
        // incoming values directly to the backing fields instead of the property setters, so this does not re-trigger
        // PushStateToService and echo the change back out
        private void OnStateChanged(string sensorId, SensorState state)
        {
            if (sensorId != SensorId) return;

            void Apply()
            {
                _isEnabled = state.Threshold.IsEnabled;
                if (state.Threshold.Value != null) _isDefaultValue = false;
                _value = state.Threshold.Value ?? _value;
                _direction = state.Threshold.Direction;
                _color = state.Threshold.Color;

                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(Value));
                OnPropertyChanged(nameof(EffectiveValue));
                OnPropertyChanged(nameof(Direction));
                OnPropertyChanged(nameof(IsAboveDirection));
                OnPropertyChanged(nameof(IsBelowDirection));
                OnPropertyChanged(nameof(Color));
                OnPropertyChanged(nameof(ColorBrush));
            }

            if (_dispatcherQueue != null) _dispatcherQueue.TryEnqueue(Apply);
            else Apply();
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
