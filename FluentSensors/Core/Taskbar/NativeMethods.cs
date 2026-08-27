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
    }
}
