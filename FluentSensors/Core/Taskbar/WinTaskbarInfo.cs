using System;
using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    // screen edge a taskbar is docked to
    public enum ScreenEdge
    {
        Left,
        Top,
        Right,
        Bottom
    }

    // snapshot of a discovered taskbar
    public record WinTaskbarInfo(
        IntPtr Hwnd,
        RectInt32 Rect,
        ScreenEdge Edge,
        uint Dpi,
        bool IsAutoHide,
        IntPtr Monitor
    );
}
