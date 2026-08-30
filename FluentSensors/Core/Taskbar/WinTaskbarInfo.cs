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
        IntPtr Hwnd, // native window handle of the taskbar (Shell_TrayWnd or Shell_SecondaryTrayWnd)
        RectInt32 Rect, // outer bounding box in physical screen coordinates
        ScreenEdge Edge, // screen edge where the taskbar is currently docked
        uint Dpi, // DPI value of the monitor containing the taskbar
        bool IsAutoHide, // whether auto-hide taskbar behavior is enabled
        IntPtr Monitor // native monitor handle hosting this taskbar
    );
}

