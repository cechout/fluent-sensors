using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;


namespace FluentSensors.Core.Taskbar
{
    // the shell state watcher:
    // explorer.exe restarts and fullscreen apps; StartWatching on the UI thread, a thread pool thread has no
    // message pump for TaskbarCreated
    // https://learn.microsoft.com/en-us/windows/win32/shell/taskbar#taskbar-creation-notification
    public class WinShellStateWatcher
    {
        // === fields ===

        // lets explorer.exe finish its new taskbar windows
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

        // a message-only window for the TaskbarCreated broadcast
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

            // HWND_MESSAGE as parent makes it message-only
            _messageWindowHwnd = NativeMethods.CreateWindowExW(
                0, MessageWindowClassName, null, 0,
                0, 0, 0, 0,
                NativeMethods.HWND_MESSAGE, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);
        }

        public void StopWatching()
        {
            if (_messageWindowHwnd == IntPtr.Zero) return;

            NativeMethods.DestroyWindow(_messageWindowHwnd);
            _messageWindowHwnd = IntPtr.Zero;
            _wndProcDelegate = null;
        }

        // fresh on every call: the foreground window covers its monitor (not the desktop, not a taskbar)
        public bool IsFullscreenAppActive()
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;

            if (foreground == NativeMethods.GetShellWindow()) return false;
            if (IsTaskbarWindow(foreground)) return false;

            if (!NativeMethods.GetWindowRect(foreground, out var windowRect)) return false;

            var monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var monitorInfo = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (!NativeMethods.GetMonitorInfoW(monitor, ref monitorInfo)) return false;

            // the monitor bounds, not the work area; fullscreen covers the taskbar too
            return windowRect.Left <= monitorInfo.rcMonitor.Left &&
                   windowRect.Top <= monitorInfo.rcMonitor.Top &&
                   windowRect.Right >= monitorInfo.rcMonitor.Right &&
                   windowRect.Bottom >= monitorInfo.rcMonitor.Bottom;
        }


        // === events ===

        // once explorer.exe recreated its taskbar windows
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

