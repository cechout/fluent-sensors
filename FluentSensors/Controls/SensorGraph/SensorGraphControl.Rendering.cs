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
    // line and area colors and threshold sections, rebuilt when values, accent, threshold or y-range change
    public sealed partial class SensorGraphControl
    {
        // fill alpha, 0-255; Flat for both surfaces with the fade off, the pairs are the gradient ends with it on
        private const byte FillAlphaFlat = 35;
        private const byte NormalFillAlphaFadeTop = 38; // the area under the line
        private const byte NormalFillAlphaFadeBottom = 10;
        private const byte AlarmFillAlphaFadeTop = 50; // the alarm zone box
        private const byte AlarmFillAlphaFadeBottom = 25;

        // lifts the alarm boxes above the series (ZIndex 0); only the faded fill paints through
        // them, the flat fill cuts holes
        private const int AlarmSectionZIndex = 1;

        // the top of the range while auto scaled and every value is 0; any positive number keeps the line at the bottom
        private const double FlatZeroYMax = 1;

        // repaint guard; the inputs of the last ApplyStroke, an unchanged signature without an alarm run is a no-op
        private readonly record struct StrokeSignature(
            Windows.UI.Color AccentColor,
            double? ThresholdValue,
            ThresholdDirection ThresholdDirection,
            Windows.UI.Color ThresholdColor,
            double YBottom,
            double YTop,
            bool IsHangingRange,
            bool HasAnyRun,
            bool FillFade);

        private StrokeSignature? _lastStrokeSignature;

        // the same for RebuildSections, without AccentColor (no effect on the sections)
        private readonly record struct SectionsSignature(
            double? ThresholdValue,
            Windows.UI.Color ThresholdColor,
            ThresholdDirection ThresholdDirection,
            bool HasAnyRun,
            bool FillFade);

        private SectionsSignature? _lastSectionsSignature;

        // one repaint past the guard; the bindings land one by one, so the first guarded repaint can
        // lock onto a half-applied mix
        private void ForceRepaint()
        {
            _lastStrokeSignature = null;
            _lastSectionsSignature = null;
            ApplyStroke();
            RebuildSections();
        }


        // the y range of the axis; 0 is the base line the fill runs to
        // positive values: 0 at the bottom, the limit or the highest point at the top
        // a series below zero (a signal in dBm): 0 at the top, the negative limit or the lowest point at the bottom, so
        // it hangs from the base line
        // every value 0 while auto scaled: an open range has no extent to scale to and the line leaves the bottom edge,
        // so the top is fixed until the first value above 0 opens the range again
        private void ApplyYRange()
        {
            double? bottom = 0;
            double? top;

            if (!IsAutoScaled)
            {
                if (ManualYMax < 0)
                {
                    bottom = ManualYMax;
                    top = 0;
                }
                else
                {
                    top = ManualYMax;
                }
            }
            else
            {
                var (lowest, highest) = ComputeValueExtent();

                if (highest > 0)
                {
                    top = null;
                }
                else if (lowest < 0)
                {
                    bottom = null;
                    top = 0;
                }
                else
                {
                    top = FlatZeroYMax;
                }
            }

            _isHangingRange = top == 0;

            // only on a change; a set limit makes the chart measure again
            if (_yAxis.MinLimit != bottom) _yAxis.MinLimit = bottom;
            if (_yAxis.MaxLimit != top) _yAxis.MaxLimit = top;
        }

        // threshold label position; on (re)appearing and on every tick while visible, so it stays on
        // the line under auto-scaling
        private void PositionThresholdLabel()
        {
            if (ThresholdValue is null) return;

            var linePixels = Chart.ScaleDataToPixels(new LvcPointD(0, ThresholdValue.Value));

            const double approxLabelHeight = 18; // ThresholdLabelBorder, roughly
            const double lineGap = 3; // line to label

            bool drawBelow = linePixels.Y < (approxLabelHeight + lineGap);
            double labelY = drawBelow
                ? linePixels.Y + lineGap
                : linePixels.Y - approxLabelHeight - lineGap;

            Canvas.SetLeft(ThresholdValueLabelBorder, 6);
            Canvas.SetTop(ThresholdValueLabelBorder, labelY);

            var (scaledValue, _) = SensorUnitFormatter.Scale(ThresholdValue.Value, SensorType);
            ThresholdValueLabelText.Text = scaledValue.ToString("0.0");
        }

        // shows the label and restarts the auto-hide timer; for threshold and scale changes, not on ticks
        private void ShowThresholdLabelBriefly()
        {
            if (!_isLoaded) return; // not measured yet; Chart_UpdateStarted calls this again

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

        // the colors of the line (Stroke) and the area under it (Fill); guarded, so an unchanged
        // tick reallocates no Skia paint
        private void ApplyStroke()
        {
            if (_lineSeries == null) return; // before the constructor finished

            // the surface both series types share
            var line = (IStrokedAndFilled)_lineSeries;

            bool hasThreshold = ThresholdValue is not null;
            var (yBottom, yTop) = hasThreshold ? ComputeCurrentYRange() : (0.0, 0.0);

            // only the flat fill follows the alarm runs, so only it repaints per tick while one is on screen
            bool hasAnyRun = !FillFade && hasThreshold && ComputeHasAnyRun();

            var signature = new StrokeSignature(AccentColor, ThresholdValue, ThresholdDirection, ThresholdColor, yBottom, yTop, _isHangingRange, hasAnyRun, FillFade);
            if (!hasAnyRun && _lastStrokeSignature == signature) return;
            _lastStrokeSignature = signature;

            var accent = new SKColor(AccentColor.R, AccentColor.G, AccentColor.B);

            // a gradient has one axis: the fade runs top to bottom and cannot cut alarm holes (RebuildSections draws
            // the boxes on top), the flat fill runs left to right and cuts itself away over the zones
            // (the fade thins out towards the base line, which a hanging range has at the top)
            line.Fill = FillFade
                ? _isHangingRange
                    ? BuildVerticalFill(accent, NormalFillAlphaFadeBottom, NormalFillAlphaFadeTop)
                    : BuildVerticalFill(accent, NormalFillAlphaFadeTop, NormalFillAlphaFadeBottom)
                : BuildFlatFillWithAlarmHoles(accent);

            // no threshold: one line color
            if (!hasThreshold)
            {
                line.Stroke = new SolidColorPaint(accent.WithAlpha(204)) { StrokeThickness = 1 };
                return;
            }

            // the line color splits at the threshold height
            var threshold = new SKColor(ThresholdColor.R, ThresholdColor.G, ThresholdColor.B);

            double ySpan = yTop - yBottom; // from the signature above
            if (ySpan <= 0) ySpan = 1;

            const double strokeOffsetPixels = 0.6; // lifts the color change point
            double chartHeight = Chart?.ActualHeight ?? 80.0;
            double yRatio = ((yTop - ThresholdValue.Value) / ySpan) + (strokeOffsetPixels / chartHeight);
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

        // top to bottom over the full height; equal alphas give the flat tone
        private static LinearGradientPaint BuildVerticalFill(SKColor color, byte topAlpha, byte bottomAlpha)
        {
            return new LinearGradientPaint(
                new[] { color.WithAlpha(topAlpha), color.WithAlpha(bottomAlpha) },
                new SKPoint(0.5f, 0),
                new SKPoint(0.5f, 1));
        }

        // the flat fill, transparent over every alarm zone so the section box behind it shows clean
        private LinearGradientPaint BuildFlatFillWithAlarmHoles(SKColor accent)
        {
            var runs = ComputeThresholdRuns();

            if (runs.Count == 0 || Values is null || Values.Count == 0)
            {
                return BuildVerticalFill(accent, FillAlphaFlat, FillAlphaFlat); // nothing to cut
            }

            // lastIndex turns a point index into a 0-1 gradient position
            int lastIndex = Values.Count - 1;
            if (lastIndex <= 0) lastIndex = 1; // no division by zero

            // colorArr[i] starts at stopArr[i]; sized up front (a start stop, 4 per run, an end stop)
            int stopCount = 2 + (runs.Count * 4);
            var colorArr = new SKColor[stopCount];
            var stopArr = new float[stopCount];
            int stopIdx = 0;

            // the normal area color on the left edge
            colorArr[stopIdx] = accent.WithAlpha(FillAlphaFlat);
            stopArr[stopIdx] = 0f;
            stopIdx++;

            foreach (var (start, end) in runs)
            {
                // a stepline sample spans i to i+1, so the hole runs from start to end+1
                float startRatio = (float)System.Math.Clamp((start + 0.0) / lastIndex, 0.0, 1.0);
                float endRatio = (float)System.Math.Clamp((end + 1.0) / lastIndex, 0.0, 1.0);

                // a hard drop to transparent at the zone start
                colorArr[stopIdx] = accent.WithAlpha(FillAlphaFlat); stopArr[stopIdx] = startRatio; stopIdx++;
                colorArr[stopIdx] = accent.WithAlpha(0); stopArr[stopIdx] = startRatio; stopIdx++;

                // and back at its end
                colorArr[stopIdx] = accent.WithAlpha(0); stopArr[stopIdx] = endRatio; stopIdx++;
                colorArr[stopIdx] = accent.WithAlpha(FillAlphaFlat); stopArr[stopIdx] = endRatio; stopIdx++;
            }

            colorArr[stopIdx] = accent.WithAlpha(FillAlphaFlat);
            stopArr[stopIdx] = 1f;

            return new LinearGradientPaint(
                colorArr,
                new SKPoint(0, 0.5f), // left to right
                new SKPoint(1, 0.5f),
                stopArr);
        }


        // the threshold line plus one full-height box per alarm zone; guarded like ApplyStroke
        private void RebuildSections()
        {
            bool hasThreshold = ThresholdValue is not null;
            bool hasAnyRun = hasThreshold && ComputeHasAnyRun();

            var signature = new SectionsSignature(ThresholdValue, ThresholdColor, ThresholdDirection, hasAnyRun, FillFade);
            if (!hasAnyRun && _lastSectionsSignature == signature) return;
            _lastSectionsSignature = signature;

            if (!hasThreshold)
            {
                Sections = Array.Empty<RectangularSection>();
                if (Chart != null) Chart.Sections = Sections;
                return;
            }

            var thresholdSk = new SKColor(ThresholdColor.R, ThresholdColor.G, ThresholdColor.B);

            // the threshold line
            var lineStroke = new SolidColorPaint(thresholdSk.WithAlpha(180))
            {
                StrokeThickness = 1,
                //PathEffect = new DashEffect(new float[] { 4, 3 }) // dashed line
            };

            // one box per alarm zone:
            // faded - on top of a fill that paints through, so it carries the fade itself
            // flat - behind a fill that cut a hole for it, one tone
            var runs = ComputeThresholdRuns();
            var boxFill = FillFade
                ? BuildVerticalFill(thresholdSk, AlarmFillAlphaFadeTop, AlarmFillAlphaFadeBottom)
                : BuildVerticalFill(thresholdSk, FillAlphaFlat, FillAlphaFlat);

            // sized up front, the line and one box per run
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
                    // start to end+1, like the hole in the flat fill
                    Xi = start - 0.0,
                    Xj = end + 1.0,
                    Yi = null, // null on both = full height
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
        // the y range as numbers, the way ApplyYRange sets it: the manual limit, or the extent when auto-scaled
        private (double Bottom, double Top) ComputeCurrentYRange()
        {
            if (!IsAutoScaled) return ManualYMax < 0 ? (ManualYMax, 0.0) : (0.0, ManualYMax);

            var (lowest, highest) = ComputeValueExtent();

            if (highest > 0) return (0, highest);
            if (lowest < 0) return (lowest, 0);
            return (0, FlatZeroYMax); // all zero
        }

        // the lowest and the highest point, both 0 without one beyond it
        private (double Lowest, double Highest) ComputeValueExtent()
        {
            double lowest = 0;
            double highest = 0;

            if (Values == null) return (lowest, highest);

            foreach (var v in Values)
            {
                if (!v.HasValue) continue;
                if (v.Value > highest) highest = v.Value;
                else if (v.Value < lowest) lowest = v.Value;
            }

            return (lowest, highest);
        }

        // the cheap check for the repaint guard; stops at the first alarm sample
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

        // one (start, end) index pair per stretch above the threshold (or below, see ThresholdDirection)
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
                    runStart = i;
                }
                else if (!isAlarm && runStart is not null)
                {
                    runs.Add((runStart.Value, i - 1));  // ended at the previous index
                    runStart = null;
                }
            }

            // still inside a zone at the end: closed at the last index
            if (runStart is not null)
            {
                runs.Add((runStart.Value, Values.Count - 1));
            }

            return runs;
        }
    }
}
