using System;
using System.Runtime.InteropServices;
using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    // the taskbar embedder:
    // makes a window a child of Shell_TrayWnd, inside the taskbar; a topmost WS_EX_NOACTIVATE window never activates,
    // so it sank below the other topmost windows (taskbar, start menu, thumbnails) and every correction flickered
    // https://github.com/zhongyang219/TrafficMonitor (the same embedding in production)
    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent
    //
    // risks:
    // cross-process SetParent joins both input queues; a UI thread hang freezes the taskbar, so long work stays off it
    // UIPI blocks messages from the unelevated explorer.exe to our elevated child; TrafficMonitor ships it fine
    internal static class WinTaskbarEmbedder
    {
        // === public methods ===

        // hwnd as a child of taskbarHwnd at screenRect; errorCode carries the Win32 error on false
        internal static bool Embed(IntPtr hwnd, IntPtr taskbarHwnd, RectInt32 screenRect, out int errorCode)
        {
            errorCode = 0;

            if (hwnd == IntPtr.Zero || taskbarHwnd == IntPtr.Zero)
            {
                return false;
            }

            // out of the topmost band first, a topmost child is invalid
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle & ~NativeMethods.WS_EX_TOPMOST);

            // popup and child exclude each other; with WS_POPUP left on it stays a top level window despite the parent
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, (style & ~NativeMethods.WS_POPUP) | NativeMethods.WS_CHILD | NativeMethods.WS_CLIPSIBLINGS);

            // style change first, then reparent
            if (NativeMethods.SetParent(hwnd, taskbarHwnd) == IntPtr.Zero)
            {
                errorCode = Marshal.GetLastWin32Error();
                return false;
            }

            Position(hwnd, taskbarHwnd, screenRect);
            return true;
        }

        // moves an embedded window, screen to parent client coordinates; AppWindow.MoveAndResize
        // works in screen coordinates
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-screentoclient
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

        // back to a top level window, before hiding, so AppWindow keeps a shape it understands
        internal static void Detach(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            NativeMethods.SetParent(hwnd, IntPtr.Zero);

            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, (style & ~NativeMethods.WS_CHILD) | NativeMethods.WS_POPUP);
        }
    }
}

