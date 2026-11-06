using System;
using Windows.Graphics;

using FluentSensors.Core.Taskbar;


namespace FluentSensors.Features.TaskbarWidget
{
    // the taskbar widget placement:
    // the screen rect of the widget from the taskbar geometry
    public static class TaskbarWidgetPlacement
    {
        // Busy's near-tray slot: end at the tray, reserve a gap after the task buttons.
        // Missing landmarks or insufficient room yield a zero-length child rather than cover shell controls.
        public static RectInt32 CalculateNearTray(WinTaskbarInfo taskbar, RectInt32? tray, RectInt32? buttons,
            int length, int innerMarginPx, int outerMarginPx, int gapPx)
        {
            int offset = 0;
            int visibleLength = 0;
            var bar = taskbar.Rect;
            if (tray is { } notificationArea && buttons is { } taskButtons)
            {
                bool vertical = taskbar.IsVertical;
                int barStart = vertical ? bar.Y : bar.X;
                int barEnd = barStart + (vertical ? bar.Height : bar.Width);
                int trayStart = vertical ? notificationArea.Y : notificationArea.X;
                int buttonsEnd = vertical ? taskButtons.Y + taskButtons.Height : taskButtons.X + taskButtons.Width;
                bool sharesBand = vertical
                    ? notificationArea.X < bar.X + bar.Width && notificationArea.X + notificationArea.Width > bar.X &&
                        taskButtons.X < bar.X + bar.Width && taskButtons.X + taskButtons.Width > bar.X
                    : notificationArea.Y < bar.Y + bar.Height && notificationArea.Y + notificationArea.Height > bar.Y &&
                        taskButtons.Y < bar.Y + bar.Height && taskButtons.Y + taskButtons.Height > bar.Y;
                if (sharesBand && length > 0 && trayStart >= barStart && trayStart <= barEnd &&
                    buttonsEnd >= barStart && buttonsEnd + gapPx <= trayStart - length)
                {
                    offset = trayStart - length - barStart;
                    visibleLength = length;
                }
            }
            return Calculate(taskbar, TaskbarAnchor.Start, offset, visibleLength, innerMarginPx, outerMarginPx);
        }

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
