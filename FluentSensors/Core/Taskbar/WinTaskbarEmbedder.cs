using System;
using System.Runtime.InteropServices;
using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    // makes a window a child of Shell_TrayWnd so it sits inside the taskbar instead of floating above it
    //
    // replaces the earlier topmost approach, which could not work by design: a WS_EX_NOACTIVATE window never
    // activates, so inside the topmost band it always ends up below every other topmost window (the taskbar
    // itself, the start menu, taskbar thumbnail previews), and every correction after the fact was visible as
    // a flicker
    // as a child there is no ordering contest left to lose, the window belongs to the taskbar
    //
    // KNOWN RISK, not resolved here:
    // SetParent across processes attaches the input queues of both threads, so a hang on our UI thread can
    // freeze the taskbar with it; anything long running has to stay off the UI thread once this is in use
    // second, FluentSensors runs elevated while explorer.exe does not, and UIPI blocks messages from the lower
    // integrity parent to our higher integrity child; TrafficMonitor ships this exact combination (elevated,
    // LibreHardwareMonitor, embedded via SetParent), which is the reason to try it, but it is not proof that a
    // WinUI 3 window with its XAML island survives the same treatment
    internal static class WinTaskbarEmbedder
    {
        // turns hwnd into a child of taskbarHwnd and places it at screenRect
        // errorCode carries the Win32 error when this returns false, so a caller can report why instead of
        // silently showing nothing
        internal static bool Embed(IntPtr hwnd, IntPtr taskbarHwnd, RectInt32 screenRect, out int errorCode)
        {
            errorCode = 0;

            if (hwnd == IntPtr.Zero || taskbarHwnd == IntPtr.Zero)
            {
                return false;
            }

            // drop out of the topmost band before becoming a child, the combination is invalid
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle & ~NativeMethods.WS_EX_TOPMOST);

            // popup and child are alternatives, not additions; leaving WS_POPUP on keeps the window behaving
            // like a top level window even after it has a parent
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, (style & ~NativeMethods.WS_POPUP) | NativeMethods.WS_CHILD);

            // style change first, then reparent; the reverse order is also seen in the wild and it is not
            // confirmed which one WinUI 3 tolerates, so this is the one to revisit first if embedding fails
            if (NativeMethods.SetParent(hwnd, taskbarHwnd) == IntPtr.Zero)
            {
                errorCode = Marshal.GetLastWin32Error();
                return false;
            }

            Position(hwnd, taskbarHwnd, screenRect);
            return true;
        }

        // moves an already embedded window, translating from screen coordinates to the parents client area
        // AppWindow.MoveAndResize must not be used once embedded, it works in screen coordinates and would
        // fight this
        internal static void Position(IntPtr hwnd, IntPtr taskbarHwnd, RectInt32 screenRect)
        {
            var origin = new NativeMethods.POINT { X = screenRect.X, Y = screenRect.Y };
            NativeMethods.ScreenToClient(taskbarHwnd, ref origin);

            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                origin.X,
                origin.Y,
                screenRect.Width,
                screenRect.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);
        }

        // detaches the window from the taskbar and makes it a plain top level window again
        // used before hiding, so AppWindow keeps operating on a shape it understands while the widget is away
        internal static void Detach(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.SetParent(hwnd, IntPtr.Zero);

            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, (style & ~NativeMethods.WS_CHILD) | NativeMethods.WS_POPUP);
        }
    }
}
