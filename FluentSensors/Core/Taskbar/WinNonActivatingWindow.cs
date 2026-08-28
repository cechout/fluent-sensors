using System;
using WinUIEx.Messaging;


namespace FluentSensors.Core.Taskbar
{
    // makes a window clickable without taking activation or keyboard focus away from the active window
    //
    // WS_EX_NOACTIVATE alone is not enough in WinUI 3: the window still activates on click despite the flag
    // WM_MOUSEACTIVATE answered with MA_NOACTIVATE is what actually works, the same mechanism used by
    // on-screen keyboards and game overlays
    // shared by TaskbarWidgetWindow and future TaskbarFlyoutWindow
    internal static class WinNonActivatingWindow
    {
        private const uint WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;

        // caller must keep the returned monitor alive in a field for as long as the window exists
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
