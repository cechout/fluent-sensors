using LiveChartsCore.Drawing;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using System.Collections.Generic;

using FluentSensors.Common;


namespace FluentSensors.Controls.SensorGraph
{
    // === color and section calculation ===
    // rebuilds line/area colors and threshold sections whenever values, accent color, threshold, or y-range change
    public sealed partial class SensorGraphControl
    {
        // threshold label positioning
        // pure positioning; called both when the label should (re)appear and on every data
        // tick while it's already visible, so auto-scaling keeps it glued to the line
        private void PositionThresholdLabel()
        {
            if (ThresholdValue is null) return;

            var linePixels = Chart.ScaleDataToPixels(new LvcPointD(0, ThresholdValue.Value));

            const double approxLabelHeight = 18; // approx rendered height of ThresholdLabelBorder
            const double lineGap = 3; // actual visual gap between the line and the label's near edge

            bool drawBelow = linePixels.Y < (approxLabelHeight + lineGap);
            double labelY = drawBelow
                ? linePixels.Y + lineGap
                : linePixels.Y - approxLabelHeight - lineGap;

            Canvas.SetLeft(ThresholdValueLabelBorder, 6);
            Canvas.SetTop(ThresholdValueLabelBorder, labelY);

            ThresholdValueLabelText.Text = ThresholdValue.Value.ToString("0.0");
        }

        // shows the label (with colors) and (re)starts the auto-hide timer; call this on
        // actual threshold/scale changes, not on routine data ticks
        private void ShowThresholdLabelBriefly()
        {
            if (!_isLoaded) return; // Chart isnt measured yet; Graph_Loaded will call this again once it is

            if (ThresholdValue is null)
            {
                _thresholdLabelTimer.Stop();
                ThresholdValueLabelBorder.Visibility = Visibility.Collapsed;
                return;
            }

            PositionThresholdLabel();

            ThresholdValueLabelBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(220, ThresholdColor.R, ThresholdColor.G, ThresholdColor.B));
            ThresholdValueLabelText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
            ThresholdValueLabelBorder.Visibility = Visibility.Visible;

            _thresholdLabelTimer.Stop();
            if (!ThresholdLabelAlwaysVisible)
            {
                _thresholdLabelTimer.Start();
            }
        }

        // color calculation
        // rebuilds the colors of the graph line (Stroke) and the area under it (Fill)
        // called whenever anything changes that affects color: values, accent color, threshold, y-range
        private void ApplyStroke()
        {
            if (_lineSeries == null) return; // guard: called before constructor finishes

            var accent = new SKColor(AccentColor.R, AccentColor.G, AccentColor.B);

            // no threshold set: flat single-color line and area
            if (ThresholdValue is null)
            {
                _lineSeries.Fill = new LinearGradientPaint(
                    new[] { accent.WithAlpha(38), accent.WithAlpha(38) },
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1));

                _lineSeries.Stroke = new SolidColorPaint(accent.WithAlpha(204)) { StrokeThickness = 1 };
                return;
            }

            // colors the graph line: split at the thresholds y-position
            var threshold = new SKColor(ThresholdColor.R, ThresholdColor.G, ThresholdColor.B);

            double yMax = ComputeCurrentYMax();
            if (yMax <= 0) yMax = 1;

            const double strokeOffsetPixels = 0.6; // moves the lines color-change point up by this many pixels
            double chartHeight = Chart?.ActualHeight ?? 80.0;
            double yRatio = 1.0 - (ThresholdValue.Value / yMax) + (strokeOffsetPixels / chartHeight);
            yRatio = System.Math.Clamp(yRatio, 0.0, 1.0);

            SKColor topColor, bottomColor;
            if (ThresholdDirection == ThresholdDirection.Above)
            {
                topColor = threshold;
                bottomColor = accent;
            }
            else
            {
                topColor = accent;
                bottomColor = threshold;
            }

            _lineSeries.Stroke = new LinearGradientPaint(
                new[] { topColor.WithAlpha(204), topColor.WithAlpha(204), bottomColor.WithAlpha(204), bottomColor.WithAlpha(204) },
                new SKPoint(0.5f, 0),
                new SKPoint(0.5f, 1),
                new[] { 0f, (float)yRatio, (float)yRatio, 1f })
            {
                StrokeThickness = 1
            };

            // colors the area under the line: cuts transparent gaps during alarm zones
            // the area itself never turns red, it just becomes invisible during alarm zones, so the red RectangularSection box
            // (built in RebuildSections) shows through cleanly
            var runs = ComputeThresholdRuns();

            if (runs.Count == 0 || Values is null || Values.Count == 0)
            {
                // no alarm zones right now: flat area color
                _lineSeries.Fill = new LinearGradientPaint(
                    new[] { accent.WithAlpha(38), accent.WithAlpha(38) },
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1));
                return;
            }

            // lastIndex turns a data point index(e.g. 12) into a 0 - 1 position for the gradient
            int lastIndex = Values.Count - 1;
            if (lastIndex <= 0) lastIndex = 1; // guard against divide-by-zero

            // colorList[i] is the color that starts at position stopList[i]; together they define the gradient
            var colorList = new System.Collections.Generic.List<SKColor>();
            var stopList = new System.Collections.Generic.List<float>();

            // gradient starts on the left edge with the normal (non-alarm) area color
            colorList.Add(accent.WithAlpha(38));
            stopList.Add(0f);

            foreach (var (start, end) in runs)
            {
                // shift the area to be removed
                // before: start-0.5 and end+0.5
                // now:    start+0.0 and end+1.0
                float startRatio = (float)System.Math.Clamp((start + 0.0) / lastIndex, 0.0, 1.0);
                float endRatio = (float)System.Math.Clamp((end + 1.0) / lastIndex, 0.0, 1.0);

                // hard drop to fully transparent at the start of the alarm zone
                colorList.Add(accent.WithAlpha(38));
                stopList.Add(startRatio);
                colorList.Add(accent.WithAlpha(0));
                stopList.Add(startRatio);

                // hard return to normal color at the end of the alarm zone
                colorList.Add(accent.WithAlpha(0));
                stopList.Add(endRatio);
                colorList.Add(accent.WithAlpha(38));
                stopList.Add(endRatio);
            }

            colorList.Add(accent.WithAlpha(38));
            stopList.Add(1f);

            _lineSeries.Fill = new LinearGradientPaint(
                colorList.ToArray(),
                new SKPoint(0, 0.5f), // horizontal gradient: left -> right
                new SKPoint(1, 0.5f),
                stopList.ToArray());
        }

        // section building
        // draws the horizontal threshold line, plus one full-height red box per alarm zone
        private void RebuildSections()
        {
            if (ThresholdValue is null)
            {
                Sections = new RectangularSection[0];
                if (Chart != null) Chart.Sections = Sections;
                return;
            }

            var sections = new System.Collections.Generic.List<RectangularSection>();
            var thresholdSk = new SKColor(ThresholdColor.R, ThresholdColor.G, ThresholdColor.B);

            // the horizontal threshold reference line
            var lineStroke = new SolidColorPaint(thresholdSk.WithAlpha(180))
            {
                StrokeThickness = 1,
                //PathEffect = new DashEffect(new float[] { 4, 3 }) // dashed line
            };
            sections.Add(new RectangularSection
            {
                Yi = ThresholdValue.Value,
                Yj = ThresholdValue.Value,
                Stroke = lineStroke,
                Fill = null
            });

            // one full-height red box per alarm zone
            var runs = ComputeThresholdRuns();
            var boxFill = new SolidColorPaint(thresholdSk.WithAlpha(38));  // same 15% alpha as normal fill

            foreach (var (start, end) in runs)
            {
                sections.Add(new RectangularSection
                {
                    // shift the area to be filled
                    // before: start-0.5 and end+0.5
                    // now:    start+0.0 and end+1.0
                    Xi = start - 0.0,
                    Xj = end + 1.0,
                    Yi = null, // y-range: null on both = full height of the chart
                    Yj = null,
                    Fill = boxFill,
                    Stroke = null
                });
            }

            Sections = sections.ToArray();
            if (Chart != null) Chart.Sections = Sections;
        }

        // shared calculation helpers
        // returns the current highest value on the y-axis:
        // the fixed ManualYMax value, or the highest visible data point when auto-scaled
        private double ComputeCurrentYMax()
        {
            if (!IsAutoScaled) return ManualYMax;

            if (Values == null || Values.Count == 0) return 100;

            double max = 0;
            foreach (var v in Values)
            {
                if (v.HasValue && v.Value > max) max = v.Value;
            }

            return max <= 0 ? 100 : max;  // fall back to a sensible range if all values are 0
        }

        // finds every time range where the value is over (or under, see ThresholdDirection) the threshold
        // returns one (startIndex, endIndex) pair per alarm zone
        private List<(int Start, int End)> ComputeThresholdRuns()
        {
            var runs = new List<(int, int)>();

            if (ThresholdValue is null || Values is null || Values.Count == 0)
                return runs;

            double threshold = ThresholdValue.Value;
            bool alarmAbove = ThresholdDirection == ThresholdDirection.Above;

            int? runStart = null;

            for (int i = 0; i < Values.Count; i++)
            {
                var v = Values[i];
                bool isAlarm = v.HasValue && (alarmAbove ? v.Value > threshold : v.Value < threshold);

                if (isAlarm && runStart is null)
                {
                    runStart = i;  // alarm zone begins here
                }
                else if (!isAlarm && runStart is not null)
                {
                    runs.Add((runStart.Value, i - 1));  // alarm zone ended at the previous index
                    runStart = null;
                }
            }

            // the data ends while still inside an alarm zone -> close it at the last index
            if (runStart is not null)
            {
                runs.Add((runStart.Value, Values.Count - 1));
            }

            return runs;
        }
    }
}