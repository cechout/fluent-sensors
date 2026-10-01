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
    )
    {
        // the small taskbar from the Windows taskbar size setting is really thinner, 32 DIP against the standard 48
        // (measured on build 26300 at 175 percent scaling), so its thickness tells it apart without reading the setting
        private const double CompactThicknessLimitDip = 40;

        // docked to the left or right screen edge, so its long axis runs top to bottom
        public bool IsVertical => Edge is ScreenEdge.Left or ScreenEdge.Right;

        public bool IsCompact => Dpi > 0 && (IsVertical ? Rect.Width : Rect.Height) * 96.0 / Dpi < CompactThicknessLimitDip;
    }
}

