using System;
using System.Threading.Tasks;
using UIA;
using UIAutomationClient;
using Windows.Graphics;

namespace FluentSensors.Core.Taskbar
{
    // Adapted from busy's taskbar/explorer.rs and taskbar/tasks.rs (MIT).
    // Copyright (c) 2026 busy contributors. See THIRD-PARTY-NOTICES.md.
    internal sealed class WinTaskbarTrayTracker
    {
        private readonly object _gate = new();
        private IntPtr _taskbar;
        private RectInt32? _buttons;
        private bool _scanPending;
        private int _generation;

        public void Reset()
        {
            lock (_gate)
            {
                _taskbar = IntPtr.Zero;
                _buttons = null;
                _generation++;
            }
        }

        // Native tray bounds are fresh each tick. UIA stays on a single in-flight worker;
        // a transient failure retains the last button bounds only for the same taskbar.
        public (RectInt32? Tray, RectInt32? Buttons) Read(WinTaskbarInfo taskbar)
        {
            var tray = ReadChild(taskbar.Hwnd, "TrayNotifyWnd");
            lock (_gate)
            {
                if (_taskbar != taskbar.Hwnd)
                {
                    _taskbar = taskbar.Hwnd;
                    _buttons = null;
                    _generation++;
                }
                if (!_scanPending)
                {
                    _scanPending = true;
                    int generation = _generation;
                    _ = Task.Run(() => ScanButtons(taskbar.Hwnd, generation));
                }
                return (tray, _buttons);
            }
        }

        private static RectInt32? ReadChild(IntPtr parent, string className)
        {
            var child = NativeMethods.FindWindowExW(parent, IntPtr.Zero, className, null);
            if (child == IntPtr.Zero || !NativeMethods.GetWindowRect(child, out var r) ||
                r.Right <= r.Left || r.Bottom <= r.Top) return null;
            return new RectInt32(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }

        private void ScanButtons(IntPtr hwnd, int generation)
        {
            RectInt32? found = null;
            try
            {
                // The COM client is local to this worker, never shared with the UI thread.
                IUIAutomation2 automation = new CUIAutomation8();
                automation.ConnectionTimeout = 500;
                automation.TransactionTimeout = 500;
                var root = automation.ElementFromHandle(hwnd);
                var condition = automation.CreatePropertyCondition(UIA_PropertyIds.UIA_ClassNamePropertyId,
                    "Taskbar.TaskbarFrameAutomationPeer");
                var frame = root.FindFirst(TreeScope.TreeScope_Descendants, condition);
                if (frame != null)
                {
                    var cache = automation.CreateCacheRequest();
                    cache.AddProperty(UIA_PropertyIds.UIA_ClassNamePropertyId);
                    cache.AddProperty(UIA_PropertyIds.UIA_BoundingRectanglePropertyId);
                    var children = frame.FindAllBuildCache(TreeScope.TreeScope_Children, automation.CreateTrueCondition(), cache);
                    for (int i = 0; i < children.Length; i++)
                    {
                        var child = children.GetElement(i);
                        string name = child.CachedClassName;
                        if (name != "ToggleButton" && name != "Taskbar.TaskListButtonAutomationPeer") continue;
                        var r = child.CachedBoundingRectangle;
                        if (r.right <= r.left || r.bottom <= r.top) continue;
                        if (found is { } previous)
                        {
                            int left = Math.Min(previous.X, r.left);
                            int top = Math.Min(previous.Y, r.top);
                            found = new RectInt32(left, top,
                                Math.Max(previous.X + previous.Width, r.right) - left,
                                Math.Max(previous.Y + previous.Height, r.bottom) - top);
                        }
                        else found = new RectInt32(r.left, r.top, r.right - r.left, r.bottom - r.top);
                    }
                }
            }
            catch
            {
                // Explorer can be restarting or its automation provider unavailable.
            }
            finally
            {
                lock (_gate)
                {
                    if (_taskbar == hwnd && _generation == generation && found != null) _buttons = found;
                    _scanPending = false;
                }
            }
        }
    }
}
