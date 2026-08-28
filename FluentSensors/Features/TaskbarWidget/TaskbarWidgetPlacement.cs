using Windows.Graphics;

using FluentSensors.Core.Taskbar;


namespace FluentSensors.Features.TaskbarWidget
{
    // pure placement math: taskbar geometry plus anchor and offset in, a screen rect out
    // no Win32 calls in here on purpose, checkable by reading it instead of by running it against a
    // real taskbar
    // MVP only, two things from the plan not wired in yet:
    // - taskbar.Rect is the GetWindowRect based bounds from WinTaskbarService (fallback tier 3), the
    //   UIA TaskbarFrame bounds (tier 1, the more accurate one) are not used here yet
    // - End does not yet subtract the system trays width, Start does not yet special case a
    //   detected widgets button; both anchors currently ignore what else already sits on the bar
    // horizontal taskbar only (Top/Bottom); a vertical one (Left/Right) is one of the conditions
    // that hides the widget entirely instead, see the Sichtbarkeitsregeln section of the plan, so it
    // is deliberately not handled here
    public static class TaskbarWidgetPlacement
    {
        // height is derived from the bar rather than passed in, so the widget keeps the same visual gap above
        // and below at any scaling instead of needing a hardcoded value per DPI
        // all values are physical pixels, the caller scales offset and verticalMargin by the taskbars DPI
        public static RectInt32 Calculate(WinTaskbarInfo taskbar, TaskbarAnchor anchor, int offset, int width, int verticalMargin)
        {
            var bar = taskbar.Rect;

            int height = bar.Height - (verticalMargin * 2);
            if (height < 1)
            {
                height = 1; // a bar thinner than the margins would otherwise give a zero or negative height
            }

            // vertically centered on the taskbars own thickness
            int y = bar.Y + (bar.Height - height) / 2;

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
