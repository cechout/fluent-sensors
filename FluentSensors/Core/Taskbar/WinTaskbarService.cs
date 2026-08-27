using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    // finds every taskbar currently on screen (the primary one, plus one Shell_SecondaryTrayWnd per additional
    // monitor) and polls their geometry
    //
    // pure detection service, knows nothing about FluentSensors itself; callers decide what a discovered taskbar
    // means for widget placement
    public class WinTaskbarService
    {
        // === fields ===

        // temp?
        private const int PollIntervalMs = 1000;

        private readonly object _lock = new();
        private List<WinTaskbarInfo> _taskbars = new();
        private CancellationTokenSource? _cts;
        private Task? _loopTask;


        // === singleton instance ===

        private static readonly WinTaskbarService _instance = new WinTaskbarService();
        public static WinTaskbarService Instance => _instance;


        // === constructor ===

        private WinTaskbarService() { }


        // === public api ===

        // latest snapshot from the polling loop, or from the most recent DiscoverNow() call if monitoring was never
        // started;
        // empty (never null) before the first discovery has run at all
        public IReadOnlyList<WinTaskbarInfo> CurrentTaskbars
        {
            get { lock (_lock) return _taskbars; }
        }

        public void StartMonitoring()
        {
            // prevent double execution
            if (_cts != null) return;

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }

        public void StopMonitoring()
        {
            if (_cts == null) return;

            _cts.Cancel();

            // block until the loop has fully exited, so once this method returns, callers can be 100% sure
            // TaskbarsChanged will never fire again
            _loopTask?.Wait(2000);

            _cts = null;
            _loopTask = null;
        }

        // one-shot discovery outside the polling loop; used by the debug dump and by any caller that just needs the
        // current state once without starting continuous monitoring
        public List<WinTaskbarInfo> DiscoverNow()
        {
            var found = FindAllTaskbars();
            lock (_lock)
            {
                _taskbars = found;
            }
            return found;
        }


        // === events ===

        // fires only when the discovered set actually changed since the previous tick (new/removed taskbar, moved
        // edge, resized, dpi change, autohide toggled); a tick where nothing changed stays silent
        public event Action<IReadOnlyList<WinTaskbarInfo>>? TaskbarsChanged;


        // === private helpers ===

        // polling loop
        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var found = FindAllTaskbars();
                bool changed;

                lock (_lock)
                {
                    changed = !_taskbars.SequenceEqual(found);
                    _taskbars = found;
                }

                // extra guard: skip the event entirely if a shutdown was requested while we were building the
                // snapshot above
                if (token.IsCancellationRequested) break;

                if (changed)
                {
                    TaskbarsChanged?.Invoke(found);
                }

                try
                {
                    await Task.Delay(PollIntervalMs, token);
                }
                catch (OperationCanceledException)
                {
                    // StopMonitoring cancelled the token while we were waiting; exit the loop cleanly here so the
                    // task completes normally instead of ending up in the Canceled state
                    break;
                }
            }
        }

        // discovery: primary taskbar (Shell_TrayWnd, exactly one) plus every secondary taskbar
        // (Shell_SecondaryTrayWnd, one per additional monitor, zero on a single-monitor system)
        private static List<WinTaskbarInfo> FindAllTaskbars()
        {
            var result = new List<WinTaskbarInfo>();

            foreach (var hwnd in FindAllWindowsByClass("Shell_TrayWnd"))
            {
                var info = BuildTaskbarInfo(hwnd);
                if (info != null) result.Add(info);
            }

            foreach (var hwnd in FindAllWindowsByClass("Shell_SecondaryTrayWnd"))
            {
                var info = BuildTaskbarInfo(hwnd);
                if (info != null) result.Add(info);
            }

            return result;
        }

        private static List<IntPtr> FindAllWindowsByClass(string className)
        {
            var result = new List<IntPtr>();
            IntPtr hwnd = IntPtr.Zero;

            while ((hwnd = NativeMethods.FindWindowExW(IntPtr.Zero, hwnd, className, null)) != IntPtr.Zero)
            {
                result.Add(hwnd);
            }

            return result;
        }

        // null if the window disappeared between being found and being queried (Explorer restart, monitor unplugged
        // mid-poll); callers just skip it for this tick rather than throwing
        private static WinTaskbarInfo? BuildTaskbarInfo(IntPtr hwnd)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return null;

            var positionData = new NativeMethods.APPBARDATA
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                hWnd = hwnd
            };
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref positionData);

            // ABM_GETSTATE reports the primary taskbars autohide state process-wide; Windows has no per-monitor
            // variant of this exact call (ABM_GETAUTOHIDEBAREX exists but takes a monitor rect, not investigated
            // yet), so every taskbar currently reports the same IsAutoHide value regardless of which monitor its on
            var stateData = new NativeMethods.APPBARDATA
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.APPBARDATA>()
            };
            uint state = (uint)NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref stateData);

            return new WinTaskbarInfo(
                Hwnd: hwnd,
                Rect: new RectInt32(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
                Edge: ToScreenEdge(positionData.uEdge),
                Dpi: NativeMethods.GetDpiForWindow(hwnd),
                IsAutoHide: (state & NativeMethods.ABS_AUTOHIDE) != 0,
                Monitor: NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST)
            );
        }

        private static ScreenEdge ToScreenEdge(uint abeEdge) => abeEdge switch
        {
            0 => ScreenEdge.Left,
            1 => ScreenEdge.Top,
            2 => ScreenEdge.Right,
            3 => ScreenEdge.Bottom,
            _ => ScreenEdge.Bottom 
        };
    }
}
