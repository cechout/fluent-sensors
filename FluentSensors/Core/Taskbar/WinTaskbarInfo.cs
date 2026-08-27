using System;
using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    // which side of the screen a taskbar is docked to; mirrors the raw ABE_LEFT/TOP/RIGHT/BOTTOM values
    // SHAppBarMessage reports
    public enum ScreenEdge
    {
        Left,
        Top,
        Right,
        Bottom
    }

    // one taskbar as discovered on a poll tick, the primary one or a secondary one on another monitor
    // a plain snapshot, not a live-updating object; two of these compare equal via normal record equality, thats
    // how WinTaskbarService decides whether anything actually changed since the previous tick
    public record WinTaskbarInfo(
        IntPtr Hwnd,

        // raw GetWindowRect bounds, includes the invisible DWM frame margin; see WinTaskbarUiaProbe (next phase)
        // for the visible-bounds comparison against this
        RectInt32 Rect,

        ScreenEdge Edge,
        uint Dpi,
        bool IsAutoHide,

        // HMONITOR handle
        IntPtr Monitor
    );
}
