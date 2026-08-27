using System;
using System.Runtime.InteropServices;


namespace FluentSensors.Core.Taskbar
{
    // raw Win32 declarations for taskbar discovery (user32 window/monitor queries, shell32 appbar messages)
    //
    // pure P/Invoke surface, no logic; WinTaskbarService is where these calls turn into WinTaskbarInfo
    internal static partial class NativeMethods
    {
        // === structs ===

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct APPBARDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }


        // === window lookup ===

        // walks top-level windows of a given class one by one via hWndChildAfter, the only way to get every match
        // since FindWindow/FindWindowEx alone always return just the first one
        // (needed to enumerate every Shell_SecondaryTrayWnd when there is more than one monitor)
        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);


        // === geometry / dpi ===

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [LibraryImport("user32.dll")]
        internal static partial uint GetDpiForWindow(IntPtr hWnd);


        // === monitor ===

        internal const uint MONITOR_DEFAULTTONEAREST = 2;

        [LibraryImport("user32.dll")]
        internal static partial IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);


        // === appbar (taskbar position / autohide state) ===

        internal const uint ABM_GETSTATE = 0x4;
        internal const uint ABM_GETTASKBARPOS = 0x5;
        internal const uint ABS_AUTOHIDE = 0x1;

        [LibraryImport("shell32.dll")]
        internal static partial IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
    }
}
