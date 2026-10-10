using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using System;
using System.Runtime.InteropServices;
using WinUIEx.Messaging;


namespace FluentSensors.Features.Widget
{
    // the desktop state of a widget window:
    // owned by the desktop window (Progman), which Win+D leaves standing together with what it owns, and held at the
    // bottom by turning every z-order change into HWND_BOTTOM; an owned window sits above its owner, so above the
    // icons and below everything else
    // off the taskbar and Alt+Tab while pinned, and its frame always drawn as inactive: the window still takes the
    // focus, but keeps the lighter shadow of a window in the background
    // an explorer.exe restart brings a new desktop window, so the owner is checked on a timer and moved over
    internal sealed partial class WinDesktopPin
    {
        // === win32 api imports ===

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static partial IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        // the desktop window, Progman
        [LibraryImport("user32.dll")]
        private static partial IntPtr GetShellWindow();

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
        private static partial IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        private static partial IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        // https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_window_corner_preference
        [LibraryImport("dwmapi.dll")]
        private static partial int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, int cbAttribute);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        private const int GWL_EXSTYLE = -20;
        private const int GWLP_HWNDPARENT = -8; // the owner of a top-level window
        private const long WS_EX_TOOLWINDOW = 0x00000080; // off Alt+Tab
        private const uint GW_OWNER = 4;
        private const uint WM_WINDOWPOSCHANGING = 0x0046;
        private const uint WM_NCACTIVATE = 0x0086; // wParam: draw the frame as active or inactive
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_DEFAULT = 0;
        private const int DWMWCP_ROUND = 2;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;


        // === fields ===

        private static readonly TimeSpan OwnerCheckInterval = TimeSpan.FromSeconds(2);

        private readonly IntPtr _hwnd;
        private readonly AppWindow _appWindow;
        private readonly DispatcherQueue _dispatcherQueue;

        private WindowMessageMonitor? _messageMonitor; // alive for as long as the window is pinned
        private DispatcherQueueTimer? _ownerCheckTimer;

        public bool IsPinned { get; private set; }


        // === constructor ===

        public WinDesktopPin(IntPtr hwnd, AppWindow appWindow, DispatcherQueue dispatcherQueue)
        {
            _hwnd = hwnd;
            _appWindow = appWindow;
            _dispatcherQueue = dispatcherQueue;
        }


        // === public methods ===

        public void Pin()
        {
            if (IsPinned) return;
            IsPinned = true;

            _appWindow.IsShownInSwitchers = false;
            SetToolWindowStyle(true);

            // a tool window gets the small corners of a popup by default; the widget keeps the 8 px of a window
            SetCornerPreference(DWMWCP_ROUND);

            _messageMonitor = new WindowMessageMonitor(_hwnd);
            _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;

            AttachToDesktop();

            // the click that pinned it left the frame active
            SendMessage(_hwnd, WM_NCACTIVATE, IntPtr.Zero, IntPtr.Zero);

            _ownerCheckTimer = _dispatcherQueue.CreateTimer();
            _ownerCheckTimer.Interval = OwnerCheckInterval;
            _ownerCheckTimer.Tick += (s, e) => AttachToDesktop();
            _ownerCheckTimer.Start();
        }

        // back to an unowned window; the caller sets the z-order it goes to
        public void Unpin()
        {
            if (!IsPinned) return;
            IsPinned = false;

            Release();

            SetWindowLongPtr(_hwnd, GWLP_HWNDPARENT, IntPtr.Zero);
            SetToolWindowStyle(false);
            SetCornerPreference(DWMWCP_DEFAULT);
            _appWindow.IsShownInSwitchers = true;

            // the frame back to what the window really is
            SendMessage(_hwnd, WM_NCACTIVATE, GetForegroundWindow() == _hwnd ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
        }

        // timer and message hook only, for a window that closes for real; its handle is not touched any more
        public void Release()
        {
            _ownerCheckTimer?.Stop();
            _ownerCheckTimer = null;

            if (_messageMonitor != null)
            {
                _messageMonitor.WindowMessageReceived -= OnWindowMessageReceived;
                _messageMonitor.Dispose();
                _messageMonitor = null;
            }
        }


        // === private helpers ===

        // owned by the current desktop window and moved to the bottom; a no-op while that already holds
        private void AttachToDesktop()
        {
            IntPtr desktop = GetShellWindow();
            if (desktop == IntPtr.Zero) return; // explorer.exe is down, the next tick tries again

            if (GetWindow(_hwnd, GW_OWNER) == desktop) return;

            SetWindowLongPtr(_hwnd, GWLP_HWNDPARENT, desktop);
            SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private void SetCornerPreference(int preference)
        {
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        private void SetToolWindowStyle(bool isToolWindow)
        {
            long exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            exStyle = isToolWindow ? exStyle | WS_EX_TOOLWINDOW : exStyle & ~WS_EX_TOOLWINDOW;
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        // a click or an activation would raise the window; the change goes through, only at the bottom
        // and the frame change of an activation is answered as if it were a deactivation
        private void OnWindowMessageReceived(object? sender, WindowMessageEventArgs e)
        {
            if (e.Message.MessageId == WM_NCACTIVATE)
            {
                e.Result = DefWindowProc(_hwnd, WM_NCACTIVATE, IntPtr.Zero, (IntPtr)e.Message.LParam);
                e.Handled = true;
                return;
            }

            if (e.Message.MessageId != WM_WINDOWPOSCHANGING) return;

            var position = Marshal.PtrToStructure<WINDOWPOS>((IntPtr)e.Message.LParam);
            if ((position.flags & SWP_NOZORDER) != 0) return;

            position.hwndInsertAfter = HWND_BOTTOM;
            Marshal.StructureToPtr(position, (IntPtr)e.Message.LParam, false);
        }
    }
}
