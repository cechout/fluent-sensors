using SkiaSharp;
using System;
using System.Collections.Generic;

using FluentSensors.Common.Sensors;


namespace FluentSensors.Features.WidgetBoard
{
    // one graph of a board widget as a PNG:
    // the board takes no XAML, so the graph is drawn the way SensorGraphControl draws it (line, fill and its fade, the
    // y range, smooth or stepline) on the element fill of the Microsoft widgets, the sport rows and weather hours
    public static class WidgetBoardRenderer
    {
        // === fields ===

        // fill alpha, 0-255; the values of SensorGraphControl
        private const byte FillAlphaFlat = 35;
        private const byte FillAlphaFadeTop = 38;
        private const byte FillAlphaFadeBottom = 10;
        private const byte LineAlpha = 204;

        // in dip; the chart overhangs the card like the one in SensorGraphControl
        private const float ChartOverhangSide = 3;
        private const float ChartOverhangBottom = 2;
        private const float CornerRadius = 4;
        private const float LineWidth = 1;

        // the element fill, measured on the board over the card background: dark 50 to 57, light 249 to 241
        private static readonly SKColor CardFillDark = new(255, 255, 255, 9);
        private static readonly SKColor CardFillLight = new(0, 0, 0, 8);

        // LineSeries.LineSmoothness of the smooth series
        private const float Smoothness = 1;


        // === public api ===

        // values oldest first; yBottom and yTop the range SensorGraphControl.ApplyYRange would set
        public static byte[] Render(
            IReadOnlyList<double?> values,
            double yBottom,
            double yTop,
            SKColor color,
            bool fillFade,
            GraphLineStyle lineStyle,
            bool isLight,
            float widthDip,
            float heightDip,
            float scale)
        {
            int width = Math.Max(1, (int)Math.Round(widthDip * scale));
            int height = Math.Max(1, (int)Math.Round(heightDip * scale));

            using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);

            var card = new SKRoundRect(new SKRect(0, 0, width, height), CornerRadius * scale);
            using (var cardPaint = new SKPaint { Color = isLight ? CardFillLight : CardFillDark, IsAntialias = true })
            {
                canvas.DrawRoundRect(card, cardPaint);
            }

            canvas.ClipRoundRect(card, SKClipOperation.Intersect, antialias: true);

            var chart = new SKRect(-ChartOverhangSide * scale, 0, width + ChartOverhangSide * scale, height + ChartOverhangBottom * scale);
            DrawSeries(canvas, chart, values, yBottom, yTop, color, fillFade, lineStyle, scale);

            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }


        // === private helpers ===

        // the fill and the line of one series into the chart rect, each run between two gaps on its own
        private static void DrawSeries(
            SKCanvas canvas,
            SKRect chart,
            IReadOnlyList<double?> values,
            double yBottom,
            double yTop,
            SKColor color,
            bool fillFade,
            GraphLineStyle lineStyle,
            float scale)
        {
            if (values.Count < 2) return;

            double span = yTop - yBottom;
            if (span <= 0) span = 1;

            float X(int index) => chart.Left + chart.Width * index / (values.Count - 1);
            float Y(double value) => chart.Top + (float)((yTop - value) / span) * chart.Height;

            // the fill runs to the base line at 0, which a hanging range has at the top
            bool isHanging = yTop == 0;
            float baseLine = Y(0);

            using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            if (fillFade)
            {
                byte top = isHanging ? FillAlphaFadeBottom : FillAlphaFadeTop;
                byte bottom = isHanging ? FillAlphaFadeTop : FillAlphaFadeBottom;
                fillPaint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, chart.Top),
                    new SKPoint(0, chart.Bottom),
                    new[] { color.WithAlpha(top), color.WithAlpha(bottom) },
                    SKShaderTileMode.Clamp);
            }
            else
            {
                fillPaint.Color = color.WithAlpha(FillAlphaFlat);
            }

            using var linePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = LineWidth * scale,
                Color = color.WithAlpha(LineAlpha)
            };

            // a missing value breaks the line, like a null point in the chart
            foreach (var run in SplitRuns(values))
            {
                var points = new List<SKPoint>(run.Count);
                foreach (int index in run) points.Add(new SKPoint(X(index), Y(values[index]!.Value)));

                using var line = lineStyle == GraphLineStyle.Smooth ? BuildSmooth(points) : BuildStepline(points);

                using var fill = new SKPath(line);
                fill.LineTo(points[^1].X, baseLine);
                fill.LineTo(points[0].X, baseLine);
                fill.Close();

                canvas.DrawPath(fill, fillPaint);
                canvas.DrawPath(line, linePaint);
            }
        }

        // the indices of every stretch without a missing value; a gap breaks the line like a null point in the chart
        private static List<List<int>> SplitRuns(IReadOnlyList<double?> values)
        {
            var runs = new List<List<int>>();
            List<int>? current = null;

            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].HasValue)
                {
                    current ??= new List<int>();
                    current.Add(i);
                }
                else if (current != null)
                {
                    runs.Add(current);
                    current = null;
                }
            }

            if (current != null) runs.Add(current);
            return runs;
        }

        // horizontal, then vertical
        private static SKPath BuildStepline(List<SKPoint> points)
        {
            var path = new SKPath();
            path.MoveTo(points[0]);

            for (int i = 1; i < points.Count; i++)
            {
                path.LineTo(points[i].X, points[i - 1].Y);
                path.LineTo(points[i]);
            }

            return path;
        }

        // the curve of a LiveCharts LineSeries: per segment, control points from the midpoints of the neighbouring
        // segments, weighted by their lengths and pulled by the smoothness
        private static SKPath BuildSmooth(List<SKPoint> points)
        {
            var path = new SKPath();
            path.MoveTo(points[0]);

            for (int i = 0; i < points.Count - 1; i++)
            {
                SKPoint p0 = points[Math.Max(0, i - 1)];
                SKPoint p1 = points[i];
                SKPoint p2 = points[i + 1];
                SKPoint p3 = points[Math.Min(points.Count - 1, i + 2)];

                var c1 = new SKPoint((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2);
                var c2 = new SKPoint((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
                var c3 = new SKPoint((p2.X + p3.X) / 2, (p2.Y + p3.Y) / 2);

                float len1 = Distance(p0, p1);
                float len2 = Distance(p1, p2);
                float len3 = Distance(p2, p3);

                float k1 = len1 + len2 == 0 ? 0 : len1 / (len1 + len2);
                float k2 = len2 + len3 == 0 ? 0 : len2 / (len2 + len3);

                var m1 = new SKPoint(c1.X + (c2.X - c1.X) * k1, c1.Y + (c2.Y - c1.Y) * k1);
                var m2 = new SKPoint(c2.X + (c3.X - c2.X) * k2, c2.Y + (c3.Y - c2.Y) * k2);

                var control1 = new SKPoint(m1.X + (c2.X - m1.X) * Smoothness + p1.X - m1.X, m1.Y + (c2.Y - m1.Y) * Smoothness + p1.Y - m1.Y);
                var control2 = new SKPoint(m2.X + (c2.X - m2.X) * Smoothness + p2.X - m2.X, m2.Y + (c2.Y - m2.Y) * Smoothness + p2.Y - m2.Y);

                path.CubicTo(control1, control2, p2);
            }

            return path;
        }

        private static float Distance(SKPoint a, SKPoint b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
