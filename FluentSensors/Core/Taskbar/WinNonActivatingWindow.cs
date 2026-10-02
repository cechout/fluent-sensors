using System;
using WinUIEx.Messaging;


namespace FluentSensors.Core.Taskbar
{
    // the non-activating window:
    // clickable without taking activation or focus, for TaskbarWidgetWindow; WS_EX_NOACTIVATE alone still
    // activates on a click in WinUI 3
    // WM_MOUSEACTIVATE answered with MA_NOACTIVATE works, as in on-screen keyboards and game overlays
    // https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mouseactivate
    internal static class WinNonActivatingWindow
    {
        // === win32 constants ===

        private const uint WM_MOUSEACTIVATE = 0x0021; // a mouse button in an inactive window
        private const int MA_NOACTIVATE = 3; // no activation, the mouse message stays


        // === public methods ===

        // the caller keeps the monitor in a field for the life of the window
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

