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
    // off the taskbar and Alt+Tab while pinned; an explorer.exe restart brings a new desktop window, so the owner is
    // checked on a timer and moved over
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
        private static partial IntPtr GetWindow(IntPtr hWnd, uint uCmd);

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
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

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

            _messageMonitor = new WindowMessageMonitor(_hwnd);
            _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;

            AttachToDesktop();

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
            _appWindow.IsShownInSwitchers = true;
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

        private void SetToolWindowStyle(bool isToolWindow)
        {
            long exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            exStyle = isToolWindow ? exStyle | WS_EX_TOOLWINDOW : exStyle & ~WS_EX_TOOLWINDOW;
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        // a click or an activation would raise the window; the change goes through, only at the bottom
        private void OnWindowMessageReceived(object? sender, WindowMessageEventArgs e)
        {
            if (e.Message.MessageId != WM_WINDOWPOSCHANGING) return;

            var position = Marshal.PtrToStructure<WINDOWPOS>((IntPtr)e.Message.LParam);
            if ((position.flags & SWP_NOZORDER) != 0) return;

            position.hwndInsertAfter = HWND_BOTTOM;
            Marshal.StructureToPtr(position, (IntPtr)e.Message.LParam, false);
        }
    }
}
