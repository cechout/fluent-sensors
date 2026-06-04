using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Microsoft.UI.Xaml;
using System.Linq;

namespace FluentHwInfo.ViewModels
{
    // INotifyPropertyChanged is important for the MVVM pattern, so that changes in the ViewModel are reflected in the UI
    public class WidgetSensorViewModel : INotifyPropertyChanged
    {
        // fields
        private const int MaxDataPoints = 80;
        public string SensorId { get; } // This is the unique hardware identifier (e.g., "/intelcpu/0/load/1")
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

        // y-axis fields
        private readonly Axis _yAxis;
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
                    UpdateYAxisLimit();
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
                    UpdateYAxisLimit();
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

        // visibility control for the left button panel field
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

        // LiveCharts fields
        public ObservableCollection<double?> SensorData { get; set; }
        public ISeries[] Series { get; set; }
        public ICartesianAxis[] XAxes { get; set; } = { new Axis { IsVisible = false } };
        public ICartesianAxis[] YAxes { get; set; } = { new Axis { IsVisible = false } };
        public LiveChartsCore.Measure.Margin ChartMargin { get; set; } = new LiveChartsCore.Measure.Margin(0);


        // constructor
        // takes the unique sensor ID and the display name as parameter, so we can display it in the UI
        public WidgetSensorViewModel(string sensorId, string sensorName)
        {
            SensorId = sensorId;
            SensorName = sensorName;
            CurrentValueText = "-"; // placeholder text until we have the first value

            // this raw data list will be plotted by LiveCharts
            SensorData = new ObservableCollection<double?>(new double?[MaxDataPoints]);

            // custom gradient fill
            var gradientFill = new LinearGradientPaint(
                new[] { SKColors.DodgerBlue.WithAlpha(70), SKColors.DodgerBlue.WithAlpha(0) },
                new SKPoint(0.5f, 0),
                new SKPoint(0.5f, 1)
            );

            // the LiveCharts ISeries definition
            Series = new ISeries[]
            {
                new LineSeries<double?>
                {
                    Values = SensorData,
                    Fill = gradientFill,
                    GeometrySize = 0, // 0: no graph points, >=1: size of graph points
                    LineSmoothness = 0.4,
                    Stroke = new SolidColorPaint(SKColors.DodgerBlue) { StrokeThickness = 2 },
                    DataPadding = new LvcPoint(0, 0) // graph padding
                }
            };

            // the LiveCharts y-axis definition
            _yAxis = new Axis
            {
                IsVisible = false,
                MinLimit = 0,     // floor always at 0
                MaxLimit = null   // null means LiveCharts does Auto-Scaling (our initial value)
            };
            YAxes = new ICartesianAxis[] { _yAxis };
        }


        public void AddDataPoint(double newValue, string formattedValueText)
        {
            // update the text in the UI (e.g. "62.5 W")
            CurrentValueText = formattedValueText;

            // shift the graph by one tick
            SensorData.RemoveAt(0);
            SensorData.Add(newValue);

            // always update the y-axis display value with each new data point
            UpdateYMaxDisplay();
        }

        // calculates, what has to be displayed in the UI as the current max value
        private void UpdateYMaxDisplay()
        {
            if (IsAutoScaled)
            {
                // finds the highest point in the graph; the ?? 0 handles the case where the list is still empty.
                double currentHighestPoint = SensorData.Max() ?? 0;
                ActualYMaxText = $"{currentHighestPoint:0.0}";
            }
            else
            {
                // if manual, we simply show the raw number
                ActualYMaxText = ManualYMax.ToString("0");
            }
        }

        // method to toggle panel visibility
        public void ToggleControlPanel()
        {
            ControlPanelVisibility = ControlPanelVisibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        // methods for the 3 buttons in the panel to control the y-axis scaling
        private void UpdateYAxisLimit()
        {
            // if Auto is on, we pass null to LiveCharts
            // if Auto is off, we pass the manual value
            _yAxis.MaxLimit = IsAutoScaled ? null : ManualYMax;
        }

        public void IncreaseYMax()
        {
            IsAutoScaled = false; // automatically turns off the auto button in the ui
            ManualYMax += 10;
        }

        public void DecreaseYMax()
        {
            IsAutoScaled = false; // automatically turns off the auto button in the ui

            // we prevent the y-axis from falling to 0 or into the negative range
            if (ManualYMax > 10)
            {
                ManualYMax -= 10;
            }
        }

        // MVVM pattern: INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}