using System;
using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    public enum ScreenEdge
    {
        Left,
        Top,
        Right,
        Bottom
    }

    // one discovered taskbar
    public record WinTaskbarInfo(
        IntPtr Hwnd, // Shell_TrayWnd or Shell_SecondaryTrayWnd
        RectInt32 Rect, // physical screen coordinates
        ScreenEdge Edge,
        uint Dpi, // of its monitor
        bool IsAutoHide,
        IntPtr Monitor
    )
    {
        // the small taskbar is 32 DIP against 48 (build 26300 at 175 percent), so the thickness tells
        // without reading the setting
        private const double CompactThicknessLimitDip = 40;

        public bool IsVertical => Edge is ScreenEdge.Left or ScreenEdge.Right;

        public bool IsCompact => Dpi > 0 && (IsVertical ? Rect.Width : Rect.Height) * 96.0 / Dpi < CompactThicknessLimitDip;
    }
}

