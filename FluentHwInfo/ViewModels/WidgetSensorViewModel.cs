using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace FluentHwInfo.ViewModels
{
    // INotifyPropertyChanged is important for the MVVM pattern, so that changes in the ViewModel are reflected in the UI
    public class WidgetSensorViewModel : INotifyPropertyChanged
    {
        // fields
        private const int MaxDataPoints = 50;
        private string _sensorName;
        public string SensorName
        {
            get => _sensorName;
            set { _sensorName = value; OnPropertyChanged(); }
        }
        private string _currentValueText;
        public string CurrentValueText
        {
            get => _currentValueText;
            set { _currentValueText = value; OnPropertyChanged(); }
        }

        // LiveCharts fields
        public ObservableCollection<double?> SensorData { get; set; }
        public ISeries[] Series { get; set; }
        public ICartesianAxis[] XAxes { get; set; } = { new Axis { IsVisible = false } };
        public ICartesianAxis[] YAxes { get; set; } = { new Axis { IsVisible = false } };
        public LiveChartsCore.Measure.Margin ChartMargin { get; set; } = new LiveChartsCore.Measure.Margin(0);

        // constructor
        // takes the sensor name (e.g. "CPU Power") as parameter, so we can display it in the UI
        public WidgetSensorViewModel(string sensorName)
        {
            SensorName = sensorName;
            CurrentValueText = "..."; // placeholder text until we have the first value

            // this raw data list will be plotted by LiveCharts
            SensorData = new ObservableCollection<double?>(new double?[MaxDataPoints]);

            // custom gradient fill
            var gradientFill = new LinearGradientPaint(
                new[] { SKColors.DodgerBlue.WithAlpha(80), SKColors.DodgerBlue.WithAlpha(0) },
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
        }

        public void AddDataPoint(double newValue, string formattedValueText)
        {
            // update the text in the UI (e.g. "62.5 W")
            CurrentValueText = formattedValueText;

            // shift the graph by one tick
            SensorData.RemoveAt(0);
            SensorData.Add(newValue);
        }

        // MVVM pattern: INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}