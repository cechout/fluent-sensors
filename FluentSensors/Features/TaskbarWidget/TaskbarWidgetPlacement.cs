using Windows.Graphics;

using FluentSensors.Core.Taskbar;


namespace FluentSensors.Features.TaskbarWidget
{
    // the taskbar widget placement:
    // the screen rect of the widget from the taskbar geometry
    public static class TaskbarWidgetPlacement
    {
        // physical pixels at the taskbar DPI
        // offset and length run along the taskbar, the margins across it:
        // inner - the side facing the desktop
        // outer - the side facing the screen edge
        public static RectInt32 Calculate(WinTaskbarInfo taskbar, TaskbarAnchor anchor, int offset, int length, int innerMarginPx, int outerMarginPx)
        {
            var bar = taskbar.Rect;

            int barLength = taskbar.IsVertical ? bar.Height : bar.Width;
            int barThickness = taskbar.IsVertical ? bar.Width : bar.Height;

            int thickness = barThickness - innerMarginPx - outerMarginPx;
            if (thickness < 1)
            {
                thickness = 1;
            }

            int along = anchor switch
            {
                TaskbarAnchor.Start => offset,
                TaskbarAnchor.End => barLength - length - offset,
                _ => offset
            };

            // the outer margin sits on the screen edge side, so it leads on the top and left edge
            int across = taskbar.Edge is ScreenEdge.Top or ScreenEdge.Left ? outerMarginPx : innerMarginPx;

            return taskbar.IsVertical
                ? new RectInt32(bar.X + across, bar.Y + along, thickness, length)
                : new RectInt32(bar.X + along, bar.Y + across, length, thickness);
        }
    }
}
