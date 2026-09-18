using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Sketches;
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
        // area fill opacity, as a 0-255 alpha on the color the surface is drawn in
        // Flat is what both surfaces show with the fill fade setting off; the two pairs are the top and bottom
        // edge of the vertical gradient each of them gets when it is on
        private const byte FillAlphaFlat = 35; // fill fade off: one tone from top to bottom
        private const byte NormalFillAlphaFadeTop = 38; // area under the line, top edge
        private const byte NormalFillAlphaFadeBottom = 10; // area under the line, bottom edge
        private const byte AlarmFillAlphaFadeTop = 50; // alarm zone box, top edge
        private const byte AlarmFillAlphaFadeBottom = 25; // alarm zone box, bottom edge

        // lifts the alarm zone boxes above the series, which sits at ZIndex 0
        // only the faded fill needs this: it paints straight through the alarm zones, so a box left behind it
        // would be covered; the flat fill cuts its own holes and keeps its boxes underneath
        private const int AlarmSectionZIndex = 1;

        // repaint guard:
        // caches the exact inputs behind the last ApplyStroke repaint
        // an unchanged signature with no active alarm run is a guaranteed no-op, native Skia paint objects stay untouched
        private readonly record struct StrokeSignature(
            Windows.UI.Color AccentColor,
            double? ThresholdValue,
            ThresholdDirection ThresholdDirection,
            Windows.UI.Color ThresholdColor,
            double YMax,
            bool HasAnyRun,
            bool FillFade);

        private StrokeSignature? _lastStrokeSignature;

        // same idea for RebuildSections
        // AccentColor does not affect section geometry so it is deliberately left out here
        private readonly record struct SectionsSignature(
            double? ThresholdValue,
            Windows.UI.Color ThresholdColor,
            ThresholdDirection ThresholdDirection,
            bool HasAnyRun,
            bool FillFade);

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

            // stroke and fill are the shared surface between the stepline and smooth series types
            var line = (IStrokedAndFilled)_lineSeries;

            bool hasThreshold = ThresholdValue is not null;
            double yMax = hasThreshold ? ComputeCurrentYMax() : 0;

            // only the flat fill follows where the alarm runs currently sit, so only it has to repaint per tick
            // while one is on screen
            bool hasAnyRun = !FillFade && hasThreshold && ComputeHasAnyRun();

            var signature = new StrokeSignature(AccentColor, ThresholdValue, ThresholdDirection, ThresholdColor, yMax, hasAnyRun, FillFade);
            if (!hasAnyRun && _lastStrokeSignature == signature) return; // identical inputs, previous paint objects still valid
            _lastStrokeSignature = signature;

            var accent = new SKColor(AccentColor.R, AccentColor.G, AccentColor.B);

            // the two fills disagree about which gradient axis they need, and a linear gradient only carries one:
            // faded runs top to bottom and therefore cannot cut alarm holes, so RebuildSections draws its boxes on
            // top of it instead; flat runs left to right and removes itself over the alarm zones, which is what
            // leaves those a clean threshold color
            line.Fill = FillFade
                ? BuildVerticalFill(accent, NormalFillAlphaFadeTop, NormalFillAlphaFadeBottom)
                : BuildFlatFillWithAlarmHoles(accent);

            // no threshold set: flat single-color line
            if (!hasThreshold)
            {
                line.Stroke = new SolidColorPaint(accent.WithAlpha(204)) { StrokeThickness = 1 };
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

            line.Stroke = new LinearGradientPaint(
                new[] { topColor.WithAlpha(204), topColor.WithAlpha(204), bottomColor.WithAlpha(204), bottomColor.WithAlpha(204) },
                new SKPoint(0.5f, 0),
                new SKPoint(0.5f, 1),
                new[] { 0f, (float)yRatio, (float)yRatio, 1f })
            {
                StrokeThickness = 1
            };
        }

        // one top-to-bottom gradient over the full height of whatever it paints
        // equal alphas give the flat single-tone surface, so both cases share this one paint shape
        private static LinearGradientPaint BuildVerticalFill(SKColor color, byte topAlpha, byte bottomAlpha)
        {
            return new LinearGradientPaint(
                new[] { color.WithAlpha(topAlpha), color.WithAlpha(bottomAlpha) },
                new SKPoint(0.5f, 0),
                new SKPoint(0.5f, 1));
        }

        // the flat area fill, which cuts itself away over every alarm zone so the RectangularSection box behind it
        // shows through cleanly; the area never turns into the threshold color itself, it just goes transparent
        private LinearGradientPaint BuildFlatFillWithAlarmHoles(SKColor accent)
        {
            var runs = ComputeThresholdRuns();

            if (runs.Count == 0 || Values is null || Values.Count == 0)
            {
                return BuildVerticalFill(accent, FillAlphaFlat, FillAlphaFlat); // no alarm zones, nothing to cut
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
            colorArr[stopIdx] = accent.WithAlpha(FillAlphaFlat);
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
                colorArr[stopIdx] = accent.WithAlpha(FillAlphaFlat); stopArr[stopIdx] = startRatio; stopIdx++;
                colorArr[stopIdx] = accent.WithAlpha(0); stopArr[stopIdx] = startRatio; stopIdx++;

                // hard return to normal color at the end of the alarm zone
                colorArr[stopIdx] = accent.WithAlpha(0); stopArr[stopIdx] = endRatio; stopIdx++;
                colorArr[stopIdx] = accent.WithAlpha(FillAlphaFlat); stopArr[stopIdx] = endRatio; stopIdx++;
            }

            colorArr[stopIdx] = accent.WithAlpha(FillAlphaFlat);
            stopArr[stopIdx] = 1f;

            return new LinearGradientPaint(
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

            var signature = new SectionsSignature(ThresholdValue, ThresholdColor, ThresholdDirection, hasAnyRun, FillFade);
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
            // faded: the box is drawn on top of a fill that paints straight through, so it carries the fade itself
            // flat: the box sits behind a fill that already cut a hole for it, so one tone is all it needs
            var boxFill = FillFade
                ? BuildVerticalFill(thresholdSk, AlarmFillAlphaFadeTop, AlarmFillAlphaFadeBottom)
                : BuildVerticalFill(thresholdSk, FillAlphaFlat, FillAlphaFlat);

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
                    Stroke = null,
                    ZIndex = FillFade ? AlarmSectionZIndex : (int?)null
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
