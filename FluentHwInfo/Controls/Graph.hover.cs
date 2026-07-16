using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using System.Collections.Generic;
using System.Collections.ObjectModel;


namespace FluentHwInfo.Controls
{
    // Graph: pointer hover interaction
    // shows a circle + value label on the chart at the pointer position
    public sealed partial class Graph
    {
        // updates hover circle + label position and value whenever the pointer moves over the chart
        private void OnChartPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _lastPointerPosition = e.GetCurrentPoint(Chart).Position;
            UpdateHoverAtPointer();
        }

        // re-usable: called on pointer move, and also on every new data point while the
        // pointer is still sitting still over the chart, so the hover value keeps tracking
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

            // circle sits exactly on the step at the cursors x-position
            Canvas.SetLeft(HoverCircle, position.X - HoverCircle.Width / 2);
            Canvas.SetTop(HoverCircle, valuePixels.Y - HoverCircle.Height / 2);

            // pick which Y coordinate the label follows, based on LabelFollowsPointer
            double labelY = LabelFollowsPointer ? position.Y : valuePixels.Y;
            CurrentValueLabelText.Text = value.Value.ToString("0.0");

            double approxHoverLabelWidth = CurrentValueLabelBorder.Width;
            const double hoverLabelGap = 6; // horizontal gap between pointer and label

            bool flipLeft = position.X + hoverLabelGap + approxHoverLabelWidth > Chart.ActualWidth;
            double labelX = flipLeft
                ? position.X - hoverLabelGap - approxHoverLabelWidth
                : position.X + hoverLabelGap;

            Canvas.SetLeft(CurrentValueLabelBorder, labelX);
            Canvas.SetTop(CurrentValueLabelBorder, labelY - 14);
        }

        // hides the hover circle and label when the pointer leaves the chart area or lands on invalid data
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

        // colors the hover circle + label: threshold color if the hovered value sits inside
        // an alarm zone, accent color otherwise
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