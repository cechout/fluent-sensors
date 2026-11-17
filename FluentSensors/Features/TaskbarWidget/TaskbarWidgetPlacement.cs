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

        // the offset that ends the widget trayGapPx before the tray (above it on a side taskbar); null when the tray
        // lies outside the taskbar or the widget would come closer than buttonGapPx to the task buttons
        // unknown buttons (the first scan is still out) skip that check
        public static int? CalculateBeforeTrayOffset(WinTaskbarInfo taskbar, RectInt32 tray, RectInt32? taskButtons, int length, int trayGapPx, int buttonGapPx)
        {
            var bar = taskbar.Rect;
            bool isVertical = taskbar.IsVertical;

            int barStart = isVertical ? bar.Y : bar.X;
            int barLength = isVertical ? bar.Height : bar.Width;
            int trayStart = (isVertical ? tray.Y : tray.X) - barStart;
            if (trayStart <= 0 || trayStart > barLength) return null;

            int offset = trayStart - trayGapPx - length;
            if (offset < 0) return null;

            if (taskButtons is RectInt32 buttons)
            {
                int buttonsEnd = (isVertical ? buttons.Y + buttons.Height : buttons.X + buttons.Width) - barStart;
                if (buttonsEnd + buttonGapPx > offset) return null;
            }

            return offset;
        }
    }
}
