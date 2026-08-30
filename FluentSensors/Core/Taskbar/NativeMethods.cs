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

        // Win32 rectangle structure defining coordinates of upper-left and lower-right corners
        // https://learn.microsoft.com/en-us/windows/win32/api/windef/ns-windef-rect
        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // contains information about a system appbar message
        // https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-appbardata
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

        // Win32 point structure defining x and y coordinates
        // https://learn.microsoft.com/en-us/windows/win32/api/windef/ns-windef-point
        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        // contains information about a display monitor
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo
        [StructLayout(LayoutKind.Sequential)]
        internal struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        // contains window class information used with RegisterClassEx
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-wndclassexw
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
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-findwindowexw
        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);


        // === geometry / dpi ===

        // retrieves the dimensions of the bounding rectangle of the specified window in screen coordinates
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        // returns the dots per inch (DPI) value for the specified window
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow
        [LibraryImport("user32.dll")]
        internal static partial uint GetDpiForWindow(IntPtr hWnd);


        // === monitor ===

        internal const uint MONITOR_DEFAULTTONEAREST = 2;

        // retrieves a handle to the display monitor that has the largest area of intersection with the bounding rectangle of a specified window
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfromwindow
        [LibraryImport("user32.dll")]
        internal static partial IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);


        // === appbar (taskbar position / autohide state) ===

        internal const uint ABM_GETSTATE = 0x4;
        internal const uint ABM_GETTASKBARPOS = 0x5;
        internal const uint ABS_AUTOHIDE = 0x1;

        // sends an appbar message to the shell
        // https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shappbarmessage
        [LibraryImport("shell32.dll")]
        internal static partial IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);


        // === message-only window (for the TaskbarCreated broadcast) ===

        // hWndParent value that creates a message-only window: not visible, no z-order, cannot be enumerated, but
        // still receives messages posted or sent directly to it, which is exactly what TaskbarCreated needs
        // https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#message-only-windows
        internal static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        // registers a new window message that is guaranteed to be unique throughout the system
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerwindowmessagew
        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial uint RegisterWindowMessageW(string lpString);

        // registers a window class for subsequent use in calls to the CreateWindowEx function
        // DllImport instead of LibraryImport, deliberate exception:
        // WNDCLASSEX has string fields with LPWStr marshalling, that makes the struct non-blittable, the LibraryImport
        // source generator does not support that for ref parameters without a custom marshaller
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerclassexw
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

        // creates an overlapped, pop-up, or child window with an extended window style
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createwindowexw
        [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr CreateWindowExW(
            uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        // destroys the specified window
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-destroywindow
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DestroyWindow(IntPtr hWnd);

        // calls the default window procedure to provide default processing for any window messages that an application does not process
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-defwindowprocw
        [LibraryImport("user32.dll")]
        internal static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // retrieves a module handle for the specified module
        // https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-getmodulehandlew
        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr GetModuleHandleW(string? lpModuleName);


        // === foreground window / fullscreen detection ===

        // retrieves a handle to the foreground window (the window with which the user is currently working)
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow
        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetForegroundWindow();

        // handle of the desktops own shell window (Progman); excluding it from fullscreen detection avoids a
        // false positive right after "Show Desktop" or whenever nothing else currently has focus
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getshellwindow
        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetShellWindow();

        // retrieves information about a display monitor
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getmonitorinfow
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);


        // === window styles (activation) ===

        internal const int GWL_EXSTYLE = -20;
        internal const int WS_EX_NOACTIVATE = 0x08000000;

        // retrieves information about the specified window
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowlongw
        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        internal static partial int GetWindowLong(IntPtr hWnd, int nIndex);

        // changes an attribute of the specified window
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowlongw
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

        // changes the parent window of the specified child window
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent
        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        // once embedded, our position counts from the parents client area, not from the screen;
        // works across processes since it is pure coordinate math, no message is sent to the parent
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-screentoclient
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_FRAMECHANGED = 0x0020; // makes a GWL_STYLE change actually take effect
        internal const uint SWP_SHOWWINDOW = 0x0040;

        // changes the size, position, and Z order of a child, pop-up, or top-level window
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);


        // === mouse tracking ===

        // used by TrackMouseEvent to track when mouse pointer leaves or hovers over a window
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-trackmouseevent
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

        // posts messages when mouse pointer leaves a window or hovers over a window for a specified amount of time
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-trackmouseevent
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

        // retrieves the cursor position in screen coordinates
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getcursorpos
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetCursorPos(out POINT lpPoint);
    }
}

