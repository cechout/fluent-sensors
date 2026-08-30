using Windows.Graphics;

using FluentSensors.Core.Taskbar;


namespace FluentSensors.Features.TaskbarWidget
{
    // calculates screen coordinates for taskbar widget placement based on taskbar geometry
    public static class TaskbarWidgetPlacement
    {
        // all dimensions and offsets are physical pixels scaled to taskbar DPI
        public static RectInt32 Calculate(WinTaskbarInfo taskbar, TaskbarAnchor anchor, int offset, int width, int topMarginPx, int bottomMarginPx)
        {
            var bar = taskbar.Rect;

            int height = bar.Height - topMarginPx - bottomMarginPx;
            if (height < 1)
            {
                height = 1;
            }

            int y = bar.Y + topMarginPx;

            int x = anchor switch
            {
                TaskbarAnchor.Start => bar.X + offset,
                TaskbarAnchor.End => bar.X + bar.Width - width - offset,
                _ => bar.X + offset
            };

            return new RectInt32(x, y, width, height);
        }
    }
}
