using System;
using WinUIEx.Messaging;


namespace FluentSensors.Core.Taskbar
{
    // makes a window clickable without it ever taking activation or keyboard focus away from
    // whatever window had it before the click
    // WS_EX_NOACTIVATE alone is not enough in WinUI 3, confirmed by hand: the window still
    // activates on click despite the flag being set
    // WM_MOUSEACTIVATE answered with MA_NOACTIVATE is what actually works, the same mechanism the
    // on screen keyboard and game overlays rely on
    // shared by TaskbarWidgetWindow and TaskbarFlyoutWindow, hence its own small class here instead
    // of living inline in either one
    internal static class WinNonActivatingWindow
    {
        private const uint WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;

        // caller must keep the returned monitor alive for as long as the window exists (store it in
        // a field), otherwise it becomes eligible for GC and the hook silently stops working
        internal static WindowMessageMonitor Apply(IntPtr hwnd)
        {
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_NOACTIVATE);

            var monitor = new WindowMessageMonitor(hwnd);
            monitor.WindowMessageReceived += (s, e) =>
            {
                if (e.Message.MessageId == WM_MOUSEACTIVATE)
                {
                    e.Handled = true;
                    e.Result = MA_NOACTIVATE;
                }
            };
            return monitor;
        }
    }
}
