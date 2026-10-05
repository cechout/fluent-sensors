using System;
using System.Runtime.InteropServices;


namespace FluentSensors.Core.Taskbar
{
    // the taskbar P/Invoke surface:
    // user32, shell32 and kernel32 declarations, no logic; WinTaskbarService turns them into WinTaskbarInfo
    internal static partial class NativeMethods
    {
        // === structs ===

        // https://learn.microsoft.com/en-us/windows/win32/api/windef/ns-windef-rect
        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

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

        // https://learn.microsoft.com/en-us/windows/win32/api/windef/ns-windef-point
        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo
        [StructLayout(LayoutKind.Sequential)]
        internal struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

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

        // goes into WNDCLASSEX.lpfnWndProc through Marshal.GetFunctionPointerForDelegate; the caller keeps the delegate
        // alive while the class is registered, or the GC collects it under the native pointer
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);


        // === window lookup ===

        // walks the top level windows of a class through hWndChildAfter, the only way past the first match
        // (every Shell_SecondaryTrayWnd)
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-findwindowexw
        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);


        // === geometry / dpi ===

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow
        [LibraryImport("user32.dll")]
        internal static partial uint GetDpiForWindow(IntPtr hWnd);


        // === monitor ===

        internal const uint MONITOR_DEFAULTTONEAREST = 2;

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfromwindow
        [LibraryImport("user32.dll")]
        internal static partial IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);


        // === appbar (taskbar position / autohide state) ===

        internal const uint ABM_GETSTATE = 0x4;
        internal const uint ABM_GETTASKBARPOS = 0x5;
        internal const uint ABS_AUTOHIDE = 0x1;

        // https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shappbarmessage
        [LibraryImport("shell32.dll")]
        internal static partial IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);


        // === message-only window (for the TaskbarCreated broadcast and the global hotkey) ===

        // the hWndParent of a message-only window: invisible and not enumerable, but it still gets messages sent to
        // it, which TaskbarCreated needs
        // https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#message-only-windows
        internal static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerwindowmessagew
        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial uint RegisterWindowMessageW(string lpString);

        // DllImport, not LibraryImport; the LPWStr fields make WNDCLASSEX non-blittable, which the generator does
        // not take for a ref parameter
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerclassexw
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createwindowexw
        [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr CreateWindowExW(
            uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-destroywindow
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DestroyWindow(IntPtr hWnd);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-defwindowprocw
        [LibraryImport("user32.dll")]
        internal static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-getmodulehandlew
        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr GetModuleHandleW(string? lpModuleName);


        // === foreground window / fullscreen detection ===

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow
        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetForegroundWindow();

        // the desktop shell window (Progman); left out of the fullscreen check, or "Show Desktop" reads as fullscreen
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getshellwindow
        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetShellWindow();

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getmonitorinfow
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);


        // === global hotkey (the flyout shortcut) ===

        internal const uint WM_HOTKEY = 0x0312;
        internal const uint MOD_NOREPEAT = 0x4000;
        internal const uint MAPVK_VK_TO_CHAR = 2;

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-unregisterhotkey
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnregisterHotKey(IntPtr hWnd, int id);

        // the character a key prints on the current layout, for the shortcut text
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-mapvirtualkeyw
        [LibraryImport("user32.dll")]
        internal static partial uint MapVirtualKeyW(uint uCode, uint uMapType);


        // === window styles (activation) ===

        internal const int GWL_EXSTYLE = -20;
        internal const int WS_EX_NOACTIVATE = 0x08000000;

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowlongw
        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        internal static partial int GetWindowLong(IntPtr hWnd, int nIndex);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowlongw
        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
        internal static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);


        // === window styles (child embedding) ===

        // a window handed to SetParent becomes a child instead of a popup, or it keeps acting top level; WS_EX_TOPMOST
        // comes off too (a child is ordered inside its parent)
        internal const int GWL_STYLE = -16;
        internal const int WS_CHILD = 0x40000000;
        internal const int WS_CLIPSIBLINGS = 0x04000000;
        internal const int WS_POPUP = unchecked((int)0x80000000);
        internal const int WS_EX_TOPMOST = 0x00000008;

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent
        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        // once embedded, positions count from the parent client area; pure math, so it works across processes
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-screentoclient
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_FRAMECHANGED = 0x0020; // applies a GWL_STYLE change
        internal const uint SWP_SHOWWINDOW = 0x0040;

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);


        // === mouse tracking ===

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

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-trackmouseevent
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getcursorpos
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetCursorPos(out POINT lpPoint);
    }
}

