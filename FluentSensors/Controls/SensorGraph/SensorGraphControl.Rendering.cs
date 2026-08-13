using LiveChartsCore.Drawing;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using System;
using System.Collections.Generic;

using FluentSensors.Common.Sensors;


namespace FluentSensors.Controls.SensorGraph
{
    // === color and section calculation ===
    // rebuilds line/area colors and threshold sections whenever values, accent color, threshold, or y-range change
    public sealed partial class SensorGraphControl
    {
        // repaint guard:
        // caches the exact inputs behind the last ApplyStroke repaint
        // an unchanged signature with no active alarm run is a guaranteed no-op, native Skia paint objects stay untouched
        private readonly record struct StrokeSignature(
            Windows.UI.Color AccentColor,
            double? ThresholdValue,
            ThresholdDirection ThresholdDirection,
            Windows.UI.Color ThresholdColor,
            double YMax,
            bool HasAnyRun);

        private StrokeSignature? _lastStrokeSignature;

        // same idea for RebuildSections
        // AccentColor does not affect section geometry so it is deliberately left out here
        private readonly record struct SectionsSignature(
            double? ThresholdValue,
            Windows.UI.Color ThresholdColor,
            ThresholdDirection ThresholdDirection,
            bool HasAnyRun);

        private SectionsSignature? _lastSectionsSignature;

        // forces one unconditional repaint, bypassing the guard above
        // needed once right after construction, Values, ThresholdValue, ThresholdDirection, ThresholdColor and
        // AccentColor all bind independently, not atomically, so the very first guarded repaint can lock onto a
        // state built from a half-applied mix of old defaults and new bound values
        private void ForceRepaint()
        {
            _lastStrokeSignature = null;
            _lastSectionsSignature = null;
            ApplyStroke();
            RebuildSections();
        }


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

            var (scaledValue, _) = SensorUnitFormatter.Scale(ThresholdValue.Value, SensorType);
            ThresholdValueLabelText.Text = scaledValue.ToString("0.0");
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
        //
        // guarded: a call whose signature exactly matches the previous one, with no alarm run currently active, is
        // skipped entirely, so the native Skia paint objects below are not reallocated on every unchanged tick
        private void ApplyStroke()
        {
            if (_lineSeries == null) return; // guard: called before constructor finishes

            bool hasThreshold = ThresholdValue is not null;
            double yMax = hasThreshold ? ComputeCurrentYMax() : 0;
            bool hasAnyRun = hasThreshold && ComputeHasAnyRun();

            var signature = new StrokeSignature(AccentColor, ThresholdValue, ThresholdDirection, ThresholdColor, yMax, hasAnyRun);
            if (!hasAnyRun && _lastStrokeSignature == signature) return; // identical inputs, previous paint objects still valid
            _lastStrokeSignature = signature;

            var accent = new SKColor(AccentColor.R, AccentColor.G, AccentColor.B);

            // no threshold set: flat single-color line and area
            if (!hasThreshold)
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

            if (yMax <= 0) yMax = 1; // yMax already computed above for the signature, reused here 

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

            // colorArr[i] is the color that starts at position stopArr[i]; together they define the gradient
            // exact final size known up front (1 start stop, 4 per alarm run, 1 end stop), plain arrays instead of
            // List<T>+ToArray skip the internal resize/copy steps on every rebuild
            int stopCount = 2 + (runs.Count * 4);
            var colorArr = new SKColor[stopCount];
            var stopArr = new float[stopCount];
            int stopIdx = 0;

            // gradient starts on the left edge with the normal (non-alarm) area color
            colorArr[stopIdx] = accent.WithAlpha(38);
            stopArr[stopIdx] = 0f;
            stopIdx++;

            foreach (var (start, end) in runs)
            {
                // shift the area to be removed
                // before: start-0.5 and end+0.5
                // now:    start+0.0 and end+1.0
                float startRatio = (float)System.Math.Clamp((start + 0.0) / lastIndex, 0.0, 1.0);
                float endRatio = (float)System.Math.Clamp((end + 1.0) / lastIndex, 0.0, 1.0);

                // hard drop to fully transparent at the start of the alarm zone
                colorArr[stopIdx] = accent.WithAlpha(38); stopArr[stopIdx] = startRatio; stopIdx++;
                colorArr[stopIdx] = accent.WithAlpha(0); stopArr[stopIdx] = startRatio; stopIdx++;

                // hard return to normal color at the end of the alarm zone
                colorArr[stopIdx] = accent.WithAlpha(0); stopArr[stopIdx] = endRatio; stopIdx++;
                colorArr[stopIdx] = accent.WithAlpha(38); stopArr[stopIdx] = endRatio; stopIdx++;
            }

            colorArr[stopIdx] = accent.WithAlpha(38);
            stopArr[stopIdx] = 1f;

            _lineSeries.Fill = new LinearGradientPaint(
                colorArr,
                new SKPoint(0, 0.5f), // horizontal gradient: left -> right
                new SKPoint(1, 0.5f),
                stopArr);
        }

        // section building
        // draws the horizontal threshold line, plus one full-height red box per alarm zone
        // guarded the same way as ApplyStroke above, an unchanged signature with no active alarm run is a no-op
        private void RebuildSections()
        {
            bool hasThreshold = ThresholdValue is not null;
            bool hasAnyRun = hasThreshold && ComputeHasAnyRun();

            var signature = new SectionsSignature(ThresholdValue, ThresholdColor, ThresholdDirection, hasAnyRun);
            if (!hasAnyRun && _lastSectionsSignature == signature) return; // identical inputs, previous sections still valid
            _lastSectionsSignature = signature;

            if (!hasThreshold)
            {
                Sections = Array.Empty<RectangularSection>();
                if (Chart != null) Chart.Sections = Sections;
                return;
            }

            var thresholdSk = new SKColor(ThresholdColor.R, ThresholdColor.G, ThresholdColor.B);

            // the horizontal threshold reference line
            var lineStroke = new SolidColorPaint(thresholdSk.WithAlpha(180))
            {
                StrokeThickness = 1,
                //PathEffect = new DashEffect(new float[] { 4, 3 }) // dashed line
            };

            // one full-height red box per alarm zone
            var runs = ComputeThresholdRuns();
            var boxFill = new SolidColorPaint(thresholdSk.WithAlpha(38));  // same 15% alpha as normal fill

            // exact final size known up front (1 threshold line, 1 box per alarm run), plain array instead of
            // List<T>+ToArray skips the internal resize/copy steps on every rebuild
            var sections = new RectangularSection[1 + runs.Count];

            sections[0] = new RectangularSection
            {
                Yi = ThresholdValue.Value,
                Yj = ThresholdValue.Value,
                Stroke = lineStroke,
                Fill = null
            };

            int sectionIdx = 1;
            foreach (var (start, end) in runs)
            {
                sections[sectionIdx++] = new RectangularSection
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
                };
            }

            Sections = sections;
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

        // cheap alarm-zone existence check for the repaint guard above
        // stops at the first alarm sample instead of walking the full list like ComputeThresholdRuns does
        private bool ComputeHasAnyRun()
        {
            if (Values is null || Values.Count == 0) return false;

            double threshold = ThresholdValue.Value;
            bool alarmAbove = ThresholdDirection == ThresholdDirection.Above;

            foreach (var v in Values)
            {
                if (v.HasValue && (alarmAbove ? v.Value > threshold : v.Value < threshold)) return true;
            }

            return false;
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