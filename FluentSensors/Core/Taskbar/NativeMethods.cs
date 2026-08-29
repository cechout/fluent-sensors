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

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszClassName;
            public IntPtr hIconSm;
        }

        // set into WNDCLASSEX.lpfnWndProc via Marshal.GetFunctionPointerForDelegate, not through direct P/Invoke
        // parameter marshalling;
        // The caller is responsible for keeping the delegate instance alive for as long as the window class stays
        // registered, otherwise the GC can collect it while native code still holds the raw pointer
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);


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


        // === message-only window (for the TaskbarCreated broadcast) ===

        // hWndParent value that creates a message-only window: not visible, no z-order, cannot be enumerated, but
        // still receives messages posted or sent directly to it, which is exactly what TaskbarCreated needs
        internal static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial uint RegisterWindowMessageW(string lpString);

        // DllImport instead of LibraryImport, deliberate exception:
        // WNDCLASSEX has string fields with LPWStr marshalling, that makes the struct non-blittable, the LibraryImport
        // source generator does not support that for ref parameters without a custom marshaller
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

        [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr CreateWindowExW(
            uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DestroyWindow(IntPtr hWnd);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr GetModuleHandleW(string? lpModuleName);


        // === foreground window / fullscreen detection ===

        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetForegroundWindow();

        // handle of the desktops own shell window (Progman); excluding it from fullscreen detection avoids a
        // false positive right after "Show Desktop" or whenever nothing else currently has focus
        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetShellWindow();

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);


        // === window styles (activation) ===

        internal const int GWL_EXSTYLE = -20;
        internal const int WS_EX_NOACTIVATE = 0x08000000;

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        internal static partial int GetWindowLong(IntPtr hWnd, int nIndex);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
        internal static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);


        // === window styles (child embedding) ===

        // a window handed to SetParent has to stop being a popup and become a child, otherwise it
        // keeps behaving like a separate top level window despite having a parent
        // WS_EX_TOPMOST has to come off at the same time: a child window is ordered inside its parent,
        // not in the systems topmost band, the two are mutually exclusive
        internal const int GWL_STYLE = -16;
        internal const int WS_CHILD = 0x40000000;
        internal const int WS_POPUP = unchecked((int)0x80000000);
        internal const int WS_EX_TOPMOST = 0x00000008;

        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        // once embedded, our position counts from the parents client area, not from the screen;
        // works across processes since it is pure coordinate math, no message is sent to the parent
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);


        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_FRAMECHANGED = 0x0020; // makes a GWL_STYLE change actually take effect
        internal const uint SWP_SHOWWINDOW = 0x0040;

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);


        // === mouse tracking ===

        [StructLayout(LayoutKind.Sequential)]
        internal struct TRACKMOUSEEVENT
        {
            public uint cbSize;
            public uint dwFlags;
            public IntPtr hwndTrack;
            public uint dwHoverTime;
        }

        internal const uint TME_LEAVE = 0x00000002;
        internal const uint TME_HOVER = 0x00000001;

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);
    }
}
