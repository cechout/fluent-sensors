using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    // finds every taskbar currently on screen (primary Shell_TrayWnd plus Shell_SecondaryTrayWnd per extra monitor)
    // and polls their geometry
    public class WinTaskbarService
    {
        // === fields ===

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

        // latest snapshot from polling loop or from most recent DiscoverNow call
        public IReadOnlyList<WinTaskbarInfo> CurrentTaskbars
        {
            get { lock (_lock) return _taskbars; }
        }

        public void StartMonitoring()
        {
            if (_cts != null) return;

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }

        public void StopMonitoring()
        {
            if (_cts == null) return;

            _cts.Cancel();
            _loopTask?.Wait(2000);

            _cts = null;
            _loopTask = null;
        }

        // one-shot discovery outside polling loop
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

        // fires only when discovered taskbar set or geometry actually changed since the previous tick
        public event Action<IReadOnlyList<WinTaskbarInfo>>? TaskbarsChanged;


        // === private helpers ===

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
                    break;
                }
            }
        }

        // queries primary (Shell_TrayWnd) plus every secondary taskbar (Shell_SecondaryTrayWnd)
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

        private static WinTaskbarInfo? BuildTaskbarInfo(IntPtr hwnd)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return null;

            var positionData = new NativeMethods.APPBARDATA
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                hWnd = hwnd
            };
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref positionData);

            // ABM_GETSTATE reports primary taskbar autohide state process-wide
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
