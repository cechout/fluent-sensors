using LiveChartsCore.Drawing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using FluentSensors.Common.Sensors;


namespace FluentSensors.Controls.SensorGraph
{
    // === pointer hover interaction ===
    // a circle and value label at the pointer position
    public sealed partial class SensorGraphControl
    {
        private void OnChartPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _lastPointerPosition = e.GetCurrentPoint(Chart).Position;
            UpdateHoverAtPointer();
        }

        // on pointer move and on every new point under a resting pointer, so the value keeps tracking
        private void UpdateHoverAtPointer()
        {
            if (Values is null || Values.Count == 0)
            {
                HideHoverElements();
                return;
            }

            var position = _lastPointerPosition;
            var dataPoint = Chart.ScalePixelsToData(new LvcPointD(position.X, position.Y));

            int index = (int)System.Math.Floor(dataPoint.X);
            if (index < 0 || index >= Values.Count)
            {
                HideHoverElements();
                return;
            }

            var value = Values[index];
            if (value is null)
            {
                HideHoverElements();
                return;
            }

            if (!_isPointerOverChart)
            {
                _isPointerOverChart = true;
                ShowHoverElements();
            }

            bool isAlarm = ThresholdValue is not null && (ThresholdDirection == ThresholdDirection.Above
                ? value.Value > ThresholdValue.Value
                : value.Value < ThresholdValue.Value);
            ApplyHoverColor(isAlarm);

            var valuePixels = Chart.ScaleDataToPixels(new LvcPointD(dataPoint.X, value.Value));

            // on the step at the cursor x
            Canvas.SetLeft(HoverCircle, position.X - HoverCircle.Width / 2);
            Canvas.SetTop(HoverCircle, valuePixels.Y - HoverCircle.Height / 2);

            double labelY = LabelFollowsPointer ? position.Y : valuePixels.Y;
            var (scaledValue, _) = SensorUnitFormatter.Scale(value.Value, SensorType);
            CurrentValueLabelText.Text = scaledValue.ToString("0.0");

            // a synchronous measure, so the flip below sees the new text width and not the last frame
            CurrentValueLabelBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double labelWidth = CurrentValueLabelBorder.DesiredSize.Width;

            const double hoverLabelGap = 6; // pointer to label

            bool flipLeft = position.X + hoverLabelGap + labelWidth > Chart.ActualWidth;
            double labelX = flipLeft
                ? position.X - hoverLabelGap - labelWidth
                : position.X + hoverLabelGap;

            Canvas.SetLeft(CurrentValueLabelBorder, labelX);
            Canvas.SetTop(CurrentValueLabelBorder, labelY - 14);
        }

        // also on invalid data
        private void OnChartPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            HideHoverElements();
        }

        private void HideHoverElements()
        {
            _isPointerOverChart = false;
            HoverCircle.Visibility = Visibility.Collapsed;
            CurrentValueLabelBorder.Visibility = Visibility.Collapsed;
        }

        private void ShowHoverElements()
        {
            HoverCircle.Visibility = Visibility.Visible;
            CurrentValueLabelBorder.Visibility = Visibility.Visible;
        }

        // the threshold color inside an alarm zone, the accent otherwise
        private void ApplyHoverColor(bool isAlarm)
        {
            var color = isAlarm ? ThresholdColor : AccentColor;

            HoverCircle.Fill = new SolidColorBrush(color);
            HoverCircle.Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(220, 255, 255, 255));

            CurrentValueLabelBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(220, color.R, color.G, color.B));
            CurrentValueLabelText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        }
    }
}
