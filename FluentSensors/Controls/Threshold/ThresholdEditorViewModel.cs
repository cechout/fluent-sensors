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
    // the threshold editor:
    // one sensor threshold (enabled, value, direction, color), synced through SensorStateService, so an edit in the
    // sensor row or a graph panel shows everywhere
    public class ThresholdEditorViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // both in the displayed base unit, see SensorTypeProfile
        private readonly double _thresholdStep;
        private readonly double _thresholdDefault;

        private readonly DispatcherQueue _dispatcherQueue;


        // === constructor ===

        public ThresholdEditorViewModel(string sensorId, string sensorType)
        {
            SensorId = sensorId;
            SensorType = sensorType;

            // step per sensor type; (a clock needs a far bigger step than a load)
            var profile = SensorTypeProfiles.GetProfile(sensorType);
            _thresholdStep = profile.ThresholdStep;
            _thresholdDefault = profile.ThresholdDefault;

            // the saved threshold; a null Value was never touched and takes the default of the sensor type
            var existingThreshold = SensorStateService.Instance.GetState(sensorId).Threshold;
            _isEnabled = existingThreshold.IsEnabled;
            _value = existingThreshold.Value ?? ResolveDefaultValue();
            _direction = existingThreshold.Direction;
            _color = existingThreshold.Color;

            // the UI thread of this editor, for updates from another window
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            SensorStateService.Instance.StateChanged += OnStateChanged;
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

        // the threshold the graphs draw; null when disabled
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
                    // back to checked; the direction is a radio choice, there is no off
                    OnPropertyChanged(nameof(IsAboveDirection));
                    return;
                }

                // a TwoWay x:Bind resync (after an Unloaded/Loaded cycle) calls this too; re-confirming the direction
                // must not re-enable the threshold
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
                const byte swatchAlpha = 200;
                return new SolidColorBrush(Windows.UI.Color.FromArgb(swatchAlpha, Color.R, Color.G, Color.B));
            }
        }

        // unused; not bound anywhere
        public Brush AboveDirectionBrush => GetDirectionBrush(ThresholdDirection.Above);
        public Brush BelowDirectionBrush => GetDirectionBrush(ThresholdDirection.Below);
        private Brush GetDirectionBrush(ThresholdDirection buttonDirection)
        {
            bool isActive = Direction == buttonDirection;
            string resourceKey = isActive ? "AccentFillColorDefaultBrush" : "ControlFillColorDefaultBrush";
            return (Brush)Application.Current.Resources[resourceKey];
        }


        // === public methods ===

        // the increase and decrease buttons
        public void Increase()
        {
            IsEnabled = true; // an adjustment enables it
            Value += SensorUnitFormatter.ToRawValue(_thresholdStep, SensorType);
        }

        public void Decrease()
        {
            IsEnabled = true;

            // never down to 0 or below
            double step = SensorUnitFormatter.ToRawValue(_thresholdStep, SensorType);
            if (Value > step)
            {
                Value -= step;
            }
        }

        // for SensorRowViewModel and SensorGraphViewModel
        public bool IsBreached(double value)
        {
            if (!IsEnabled) return false;

            return Direction == ThresholdDirection.Above
                ? value > Value
                : value < Value;
        }

        // called from the Cleanup of the owner, or this keeps reacting after its row or graph is gone
        public void Cleanup()
        {
            SensorStateService.Instance.StateChanged -= OnStateChanged;
        }


        // === private helpers ===

        // the default of the sensor type, in the active data unit
        private double ResolveDefaultValue()
        {
            return SensorUnitFormatter.ToRawValue(_thresholdDefault, SensorType);
        }

        private void PushStateToService()
        {
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

        // a change made elsewhere, into the backing fields, so PushStateToService does not echo it
        // a null value is the per-type default, which is how a data unit switch
        // (SensorStateService.ResetUnitDependentValues) lands on a round number
        private void OnStateChanged(string sensorId, SensorState state)
        {
            if (sensorId != SensorId) return;

            void Apply()
            {
                _isEnabled = state.Threshold.IsEnabled;
                _value = state.Threshold.Value ?? ResolveDefaultValue();
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
