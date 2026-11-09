using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;

namespace FluentSensors.Features.TaskbarWidget
{
    internal static class SensorTrayIconRenderer
    {
        internal static Icon Render(string text, double raw, string sensorType, bool light, Color? foreground = null, Color? background = null)
        {
            int size = Math.Clamp(GetSystemMetricsForDpi(49, GetDpiForSystem()), 16, 64);
            using var bitmap = RenderBitmap(text, raw, sensorType, light, size, foreground, background);
            IntPtr handle = bitmap.GetHicon();
            try { using var icon = Icon.FromHandle(handle); return (Icon)icon.Clone(); }
            finally { DestroyIcon(handle); }
        }

        internal static Bitmap RenderBitmap(string text, double raw, string sensorType, bool light, int size, Color? foreground = null, Color? background = null)
        {
            var bitmap = new Bitmap(size, size);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.ScaleTransform(size / 64f, size / 64f);
            graphics.Clear(Color.Transparent);
            using var card = new GraphicsPath();
            const float radius = 20;
            card.AddArc(0, 0, radius, radius, 180, 90);
            card.AddArc(64 - radius, 0, radius, radius, 270, 90);
            card.AddArc(64 - radius, 64 - radius, radius, radius, 0, 90);
            card.AddArc(0, 64 - radius, radius, radius, 90, 90);
            card.CloseFigure();
            using var fill = new SolidBrush(background ?? (light ? Color.FromArgb(180, 235, 235, 235) : Color.FromArgb(180, 55, 58, 60)));
            graphics.FillPath(fill, card);
            using var ink = new SolidBrush(foreground ?? (light ? Color.FromArgb(25, 25, 25) : Color.White));
            string value = text;
            if (text != "-")
            {
                if (sensorType is "Temperature" or "Load" or "Level")
                    value = raw.ToString("0", CultureInfo.CurrentCulture);
                else if (sensorType == "Throughput")
                {
                    double scaled = raw < 1 ? raw * 1024 : raw >= 1024 ? raw / 1024 : raw;
                    value = scaled.ToString(scaled < 10 ? "0.#" : "0", CultureInfo.CurrentCulture);
                }
                else
                {
                    // The formatted value retains sensor-specific scaling; units stay in the tooltip.
                    int unitStart = text.IndexOf(' ');
                    if (unitStart >= 0) value = text[..unitStart];
                }
            }
            DrawGlyphs(graphics, ink, value, new RectangleF(9, 12, 46, 40));
            return bitmap;
        }

        // Fit the visible outlines with one scale for both axes, retaining the font's proportions.
        // Center inside the padded content rectangle rather than filling the icon edge to edge.
        private static void DrawGlyphs(Graphics graphics, Brush ink, string text, RectangleF target)
        {
            using var path = new GraphicsPath(FillMode.Winding);
            using var family = CreateFontFamily();
            path.AddString(text, family, (int)FontStyle.Bold, 64, PointF.Empty, StringFormat.GenericTypographic);
            var bounds = path.GetBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            float scale = Math.Min(target.Height / bounds.Height, target.Width / bounds.Width);
            using var transform = new Matrix(scale, 0, 0, scale,
                target.X + (target.Width - bounds.Width * scale) / 2 - bounds.X * scale,
                target.Y + (target.Height - bounds.Height * scale) / 2 - bounds.Y * scale);
            path.Transform(transform);
            graphics.FillPath(ink, path);
        }

        private static FontFamily CreateFontFamily()
        {
            try { return new FontFamily("Bahnschrift SemiCondensed"); }
            catch (ArgumentException) { return new FontFamily("Arial"); }
        }

        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    }
}
