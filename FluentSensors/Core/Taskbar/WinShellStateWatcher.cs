using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;


namespace FluentSensors.Core.Taskbar
{
    // monitors explorer.exe restart broadcasts and active fullscreen applications
    //
    // StartWatching must be called from the UI thread, not from a background Task:
    // the TaskbarCreated broadcast only reaches this class through the calling threads Win32 message pump;
    // a message-only window on a thread pool thread has no pump and would never receive messages
    // https://learn.microsoft.com/en-us/windows/win32/shell/taskbar#taskbar-creation-notification
    public class WinShellStateWatcher
    {
        // === fields ===

        // gives explorer.exe time to finish creating its new taskbar windows before querying
        private const int StabilizationDelayMs = 1500;

        private const string MessageWindowClassName = "FluentSensorsShellStateWatcher";

        private IntPtr _messageWindowHwnd;
        private NativeMethods.WndProc? _wndProcDelegate;
        private uint _taskbarCreatedMessageId;


        // === singleton instance ===

        private static readonly WinShellStateWatcher _instance = new WinShellStateWatcher();
        public static WinShellStateWatcher Instance => _instance;


        // === constructor ===

        private WinShellStateWatcher() { }


        // === public api ===

        // registers a message-only window and begins listening for the system TaskbarCreated broadcast
        public void StartWatching()
        {
            if (_messageWindowHwnd != IntPtr.Zero) return;

            _taskbarCreatedMessageId = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
            _wndProcDelegate = HandleWindowMessage;

            var wndClass = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                hInstance = NativeMethods.GetModuleHandleW(null),
                lpszClassName = MessageWindowClassName
            };
            NativeMethods.RegisterClassExW(ref wndClass);

            // no visible surface, no title, no size: HWND_MESSAGE parent makes it message-only
            _messageWindowHwnd = NativeMethods.CreateWindowExW(
                0, MessageWindowClassName, null, 0,
                0, 0, 0, 0,
                NativeMethods.HWND_MESSAGE, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);
        }

        // tears down the message-only window and stops listening for broadcast messages
        public void StopWatching()
        {
            if (_messageWindowHwnd == IntPtr.Zero) return;

            NativeMethods.DestroyWindow(_messageWindowHwnd);
            _messageWindowHwnd = IntPtr.Zero;
            _wndProcDelegate = null;
        }

        // computed fresh on every call: checks if foreground window covers full monitor bounds (excluding desktop and taskbars)
        public bool IsFullscreenAppActive()
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;

            // the desktop itself or a taskbar cannot be a fullscreen app
            if (foreground == NativeMethods.GetShellWindow()) return false;
            if (IsTaskbarWindow(foreground)) return false;

            if (!NativeMethods.GetWindowRect(foreground, out var windowRect)) return false;

            var monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var monitorInfo = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (!NativeMethods.GetMonitorInfoW(monitor, ref monitorInfo)) return false;

            // covers full monitor bounds, not just work area, since fullscreen apps draw over the taskbar area too
            return windowRect.Left <= monitorInfo.rcMonitor.Left &&
                   windowRect.Top <= monitorInfo.rcMonitor.Top &&
                   windowRect.Right >= monitorInfo.rcMonitor.Right &&
                   windowRect.Bottom >= monitorInfo.rcMonitor.Bottom;
        }


        // === events ===

        // fires once explorer.exe has finished recreating its taskbar windows after a restart
        public event Action? ExplorerRestarted;


        // === private helpers ===

        private IntPtr HandleWindowMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == _taskbarCreatedMessageId)
            {
                _ = RaiseExplorerRestartedAfterStabilizationAsync();
                return IntPtr.Zero;
            }

            return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private async Task RaiseExplorerRestartedAfterStabilizationAsync()
        {
            await Task.Delay(StabilizationDelayMs);
            ExplorerRestarted?.Invoke();
        }

        private static bool IsTaskbarWindow(IntPtr hwnd)
        {
            foreach (var taskbar in WinTaskbarService.Instance.CurrentTaskbars)
            {
                if (taskbar.Hwnd == hwnd) return true;
            }
            return false;
        }
    }
}

