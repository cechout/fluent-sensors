using System;
using System.Threading.Tasks;
using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    // the taskbar tray tracker:
    // where the notification area of a taskbar starts and where its task buttons lie, so the widget never covers a
    // button and can sit right before the tray; the approach follows busy (MIT, https://github.com/aivanyuk/busy)
    // - the tray is a Win32 child window (TrayNotifyWnd), read fresh on every call
    // - the task buttons only exist in UIA, read on a worker, one scan at a time; a call returns the last result
    public sealed class WinTaskbarTrayTracker
    {
        // === fields ===

        private const string TrayClassName = "TrayNotifyWnd"; // only on Shell_TrayWnd, a secondary taskbar has none

        private readonly object _lock = new();
        private IntPtr _taskbarHwnd;
        private RectInt32? _taskButtons;
        private bool _isScanPending;
        private int _generation; // drops a scan that started before a reset or a taskbar change


        // === public api ===

        // the tray rect and the last known task button extent, both in physical screen pixels; the tray is null on a
        // taskbar without one, the buttons until the first scan came back
        public (RectInt32? Tray, RectInt32? TaskButtons) Read(IntPtr taskbarHwnd)
        {
            var tray = ReadTray(taskbarHwnd);

            lock (_lock)
            {
                if (_taskbarHwnd != taskbarHwnd)
                {
                    _taskbarHwnd = taskbarHwnd;
                    _taskButtons = null;
                    _generation++;
                }

                if (!_isScanPending)
                {
                    _isScanPending = true;
                    int generation = _generation;
                    _ = Task.Run(() => ScanTaskButtons(taskbarHwnd, generation));
                }

                return (tray, _taskButtons);
            }
        }

        // forgets the buttons, so the next Read waits for a fresh scan
        public void Reset()
        {
            lock (_lock)
            {
                _taskbarHwnd = IntPtr.Zero;
                _taskButtons = null;
                _generation++;
            }
        }


        // === private helpers ===

        private static RectInt32? ReadTray(IntPtr taskbarHwnd)
        {
            var tray = NativeMethods.FindWindowExW(taskbarHwnd, IntPtr.Zero, TrayClassName, null);
            if (tray == IntPtr.Zero || !NativeMethods.GetWindowRect(tray, out var rect)) return null;
            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) return null;

            return new RectInt32(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        // a failed scan keeps the last extent of the same taskbar; explorer.exe may be restarting
        private void ScanTaskButtons(IntPtr taskbarHwnd, int generation)
        {
            var found = WinTaskbarUiaProbe.Instance.FindTaskButtons(taskbarHwnd);

            lock (_lock)
            {
                if (found != null && _taskbarHwnd == taskbarHwnd && _generation == generation)
                {
                    _taskButtons = found;
                }
                _isScanPending = false;
            }
        }
    }
}
