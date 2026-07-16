using FluentHwInfo.Services;
using FluentHwInfo.Views;
using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using FluentHwInfo.Controls;

namespace FluentHwInfo.ViewModels
{
    public class WidgetSensorViewModel : INotifyPropertyChanged
    {
        // general fields
        public ObservableCollection<double?> SensorData { get; private set; }
        public string SensorId { get; } // this is the unique sensor identifier (e.g., "/intelcpu/0/load/1")
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

        // y-axis fields
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
                }
            }
        }
        public double? ThresholdValue => IsThresholdEnabled ? _manualThreshold : (double?)null;
        private ThresholdDirection _thresholdDirection = ThresholdDirection.Above;
        public ThresholdDirection ThresholdDirection
        {
            get => _thresholdDirection;
            set { _thresholdDirection = value; OnPropertyChanged(); }
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
            }
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


        // constructor
        public WidgetSensorViewModel(string sensorId, string sensorName)
        {
            SensorId = sensorId;
            SensorName = sensorName;
            CurrentValueText = "-"; // placeholder text until we have the first value

            // this raw data list will be plotted by LiveCharts
            // we use LINQ Enumerable.Repeat to fill the entire list with "0.0" values at startup
            SensorData = new ObservableCollection<double?>(Enumerable.Repeat<double?>(0.0, SettingsService.Instance.GraphDataPoints));

            GraphColor = ResolveGraphColor( SettingsService.Instance.UseGraphAccentColor, SettingsService.Instance.GraphCustomColor);
            SettingsService.Instance.GraphColorChanged += OnGraphColorChanged;
            SettingsService.Instance.GraphDataPointsChanged += OnGraphDataPointsChanged;
        }


        // very important; so so our program does not fuck up again
        public void Cleanup()
        {
            SettingsService.Instance.GraphColorChanged -= OnGraphColorChanged;
            SettingsService.Instance.GraphDataPointsChanged -= OnGraphDataPointsChanged;
        }


        // data processing
        public void AddDataPoint(double newValue, string formattedValueText)
        {
            // update the current value text
            CurrentValueText = formattedValueText;

            // shift the graph by one tick
            SensorData.RemoveAt(0);
            SensorData.Add(newValue);

            // update y-axis display value with each new data point
            UpdateYMaxDisplay();
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


        // service listener
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
            ManualYMax += 10;
        }
        public void DecreaseYMax()
        {
            IsAutoScaled = false; // automatically turns off the auto button in the ui

            // preventing the y-axis from falling to 0 or into the negative range
            if (ManualYMax > 10)
            {
                ManualYMax -= 10;
            }
        }
        // threshold buttons
        public void IncreaseThreshold()
        {
            IsThresholdEnabled = true;  // auto-enable when the user adjusts the value
            ManualThreshold += 5;
        }
        public void DecreaseThreshold()
        {
            IsThresholdEnabled = true;  // auto-enable when the user adjusts the value

            // preventing the threshold from falling to 0 or into the negative range
            if (ManualThreshold > 5)
            {
                ManualThreshold -= 5;
            }
        }


        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}