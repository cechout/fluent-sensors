using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Runtime.InteropServices.WindowsRuntime;


namespace FluentSensors.Common.UI
{
    // the acrylic grain of one window:
    // a canvas host with one rectangle, under every surface fill; the taskbar flyout, the widget and the csv logger
    // window each own one
    //
    // the acrylic recipe ends with a 2 percent noise layer (sc_noiseOpacity) that the backdrop controller does
    // not draw; painted here by hand, one random grayscale bitmap under every surface fill:
    // https://github.com/microsoft/microsoft-ui-xaml/blob/6aed8d97fdecfe9b19d70c36bd1dacd9c6add7c1/dev/Materials/Acrylic/AcrylicBrush.h
    public sealed class AcrylicGrain
    {
        // === fields ===

        private const int NoiseSeed = 0x5EED;

        // opacity stays the recipe constant, the strength is the standard deviation around a mean of 128, in screen
        // levels (so tuning the grain never moves the calibrated colors); a bell like the native grain, about six
        // levels wide, sd 1.03 on the dark surfaces
        private const double NoiseLayerOpacity = 0.02;
        private const double NoiseDeviationLevels = 1.0;

        private readonly UIElement _host;
        private readonly Rectangle _overlay;
        private readonly FrameworkElement _scaleSource;

        private WriteableBitmap? _noiseBitmap;
        private double _noiseScale;


        // === constructor ===

        // scaleSource is any element of the window, read for its rasterization scale
        public AcrylicGrain(UIElement host, Rectangle overlay, FrameworkElement scaleSource)
        {
            _host = host;
            _overlay = overlay;
            _scaleSource = scaleSource;
        }


        // === public methods ===

        public bool IsVisible => _host.Visibility == Visibility.Visible;

        // shown at the given size, or hidden
        public void Show(bool visible, double widthDip, double heightDip)
        {
            _host.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible)
            {
                Ensure(widthDip, heightDip);
            }
        }

        // grows on demand, never shrinks; sized in physical pixels and scaled back down, so one noise pixel lands on
        // one physical pixel instead of being smeared into coarse grain
        public void Ensure(double widthDip, double heightDip)
        {
            if (widthDip <= 0 || heightDip <= 0) return;

            double scale = _scaleSource.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0) scale = 1.0;

            int width = (int)Math.Ceiling(widthDip * scale);
            int height = (int)Math.Ceiling(heightDip * scale);

            bool scaleUnchanged = Math.Abs(scale - _noiseScale) < 0.001;
            if (_noiseBitmap != null && scaleUnchanged
                && _noiseBitmap.PixelWidth >= width && _noiseBitmap.PixelHeight >= height)
            {
                return;
            }

            if (scaleUnchanged)
            {
                width = Math.Max(width, _noiseBitmap?.PixelWidth ?? 0);
                height = Math.Max(height, _noiseBitmap?.PixelHeight ?? 0);
            }

            var bitmap = new WriteableBitmap(width, height);
            var random = new Random(NoiseSeed);
            var pixels = new byte[width * height * 4];

            double deviation = NoiseDeviationLevels / NoiseLayerOpacity;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                // Box-Muller
                double gaussian = Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble());
                byte level = (byte)Math.Clamp(Math.Round(128 + (gaussian * deviation)), 0, 255);
                pixels[i] = level;
                pixels[i + 1] = level;
                pixels[i + 2] = level;
                pixels[i + 3] = 255;
            }

            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(pixels, 0, pixels.Length);
            }
            bitmap.Invalidate();

            _noiseBitmap = bitmap;
            _noiseScale = scale;

            _host.Opacity = NoiseLayerOpacity;
            _overlay.Width = width;
            _overlay.Height = height;
            _overlay.RenderTransform = new ScaleTransform
            {
                ScaleX = 1.0 / scale,
                ScaleY = 1.0 / scale
            };

            // Stretch None; any stretching smears the grain
            _overlay.Fill = new ImageBrush
            {
                ImageSource = bitmap,
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };
        }
    }
}
