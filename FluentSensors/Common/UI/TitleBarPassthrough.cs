using System;
using System.Collections.Generic;
using System.Diagnostics;

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;

using Windows.Foundation;
using Windows.Graphics;


namespace FluentSensors.Common.UI
{
    // the title bar passthrough:
    // SetTitleBar makes the whole bar non-client, so an interactive element there needs a passthrough rect or a
    // press only drags the window
    public static class TitleBarPassthrough
    {
        // absolute window coordinates, so again on every layout of the bar and every element
        // that moves, appears or goes
        public static void Apply(Window window, FrameworkElement titleBar, params FrameworkElement[] elements)
        {
            if (window == null || titleBar == null || !titleBar.IsLoaded) return;

            try
            {
                var nonClientInputSrc = InputNonClientPointerSource.GetForWindowId(window.AppWindow.Id);
                if (nonClientInputSrc == null) return;

                double scale = titleBar.XamlRoot?.RasterizationScale ?? 1.0;
                var rects = new List<RectInt32>();

                foreach (var element in elements)
                {
                    AddPassthroughRect(rects, element, scale);
                }

                nonClientInputSrc.SetRegionRects(NonClientRegionKind.Passthrough, rects.ToArray());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TitleBarPassthrough] failed: {ex.Message}");
            }
        }

        private static void AddPassthroughRect(List<RectInt32> rects, FrameworkElement element, double scale)
        {
            if (element == null || element.Visibility != Visibility.Visible || !element.IsLoaded) return;

            try
            {
                var transform = element.TransformToVisual(null);
                var bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    rects.Add(new RectInt32(
                        (int)Math.Round(bounds.X * scale),
                        (int)Math.Round(bounds.Y * scale),
                        (int)Math.Round(bounds.Width * scale),
                        (int)Math.Round(bounds.Height * scale)
                    ));
                }
            }
            catch
            {
                // an element mid-teardown gives no rect
            }
        }
    }
}
