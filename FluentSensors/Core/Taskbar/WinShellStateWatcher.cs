using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;


namespace FluentSensors.Core.Taskbar
{
    // two unrelated shell-level concerns bundled here because both are cheap, standalone, and specific to the
    // taskbar feature: knowing when explorer.exe restarted, and knowing whether a fullscreen app currently owns
    // the screen
    //
    // StartWatching must be called from the UI thread, not from a background Task
    // the TaskbarCreated broadcast only reaches this class through the calling threads own Win32 message pump
    // (WinUI already runs one on the UI thread for its own windows); a message-only window created on a thread
    // pool thread has no pump ever dispatching to it and would silently never receive anything
    public class WinShellStateWatcher
    {
        // === fields ===

        // give explorer.exe time to finish creating its new taskbar windows before anything tries to query them
        // again; the raw broadcast fires while the new windows are still mid-setup
        private const int StabilizationDelayMs = 1500;

        private const string MessageWindowClassName = "FluentSensorsShellStateWatcher";

        private IntPtr _messageWindowHwnd;
        private NativeMethods.WndProc? _wndProcDelegate; // kept alive here; see the delegate declaration in NativeMethods 
        private uint _taskbarCreatedMessageId;


        // === singleton instance ===

        private static readonly WinShellStateWatcher _instance = new WinShellStateWatcher();
        public static WinShellStateWatcher Instance => _instance;


        // === constructor ===

        private WinShellStateWatcher() { }


        // === public api ===

        public void StartWatching()
        {
            // prevent double execution
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

            // no visible surface, no title, no size: a message-only window only ever needs to exist to receive
            // messages, HWND_MESSAGE as the parent is what makes it message-only instead of a real top-level window
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

        // computed fresh every call, cheap enough (GetForegroundWindow + one GetWindowRect + one GetMonitorInfoW)
        // that this never needed its own polling loop, callers just check it whenever they need to know
        public bool IsFullscreenAppActive()
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;

            // the desktop itself or a taskbar cannot be "a fullscreen app";
            // excluding them avoids a false positive right after "Show Desktop" or whenever a taskbar happens to have
            // focus
            if (foreground == NativeMethods.GetShellWindow()) return false;
            if (IsTaskbarWindow(foreground)) return false;

            if (!NativeMethods.GetWindowRect(foreground, out var windowRect)) return false;

            var monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var monitorInfo = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (!NativeMethods.GetMonitorInfoW(monitor, ref monitorInfo)) return false;

            // covers the full monitor bounds, not just the work area: the work area already excludes the strip
            // the taskbar sits in, a genuine fullscreen app draws over that too
            return windowRect.Left <= monitorInfo.rcMonitor.Left &&
                   windowRect.Top <= monitorInfo.rcMonitor.Top &&
                   windowRect.Right >= monitorInfo.rcMonitor.Right &&
                   windowRect.Bottom >= monitorInfo.rcMonitor.Bottom;
        }


        // === events ===

        // fires once explorer.exe has had time to finish creating its new taskbar windows after a restart;
        // callers should re-run taskbar discovery at this point, not on the raw broadcast itself
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
