using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UIA;
using UIAutomationClient;
using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    // the taskbar UIA probe:
    // cross-process UIA lookups for taskbar parts without a window handle (the frame, the tray, the widgets button),
    // which GetWindowRect and SHAppBarMessage cannot see
    // optional enrichment; any query can return null and placement must still work
    public class WinTaskbarUiaProbe
    {
        // === fields ===

        // the longest a round trip to a hung explorer.exe blocks the caller; also the native
        // ConnectionTimeout and TransactionTimeout
        private const int QueryTimeoutMs = 500;

        // the taskbar structure hardly changes while explorer.exe runs
        private const int CacheDurationMs = 5000;

        // KNOWN UNRELIABLE:
        // undocumented internals of the XAML Islands taskbar of explorer.exe that can shift between Windows 11 builds,
        // confirmed against live UIA tree dumps
        // TaskbarFrame is an AutomationId, TrayNotifyWnd a classic Win32 child class name
        private const string TaskbarFrameAutomationId = "TaskbarFrame";
        private const string TrayClassName = "TrayNotifyWnd";
        private const string WidgetsButtonAutomationId = "WidgetsButton";

        private readonly object _lock = new();
        private readonly Dictionary<IntPtr, (WinTaskbarUiaSnapshot? Snapshot, DateTime QueriedAt)> _cache = new();

        private IUIAutomation2? _automation;


        // === singleton instance ===

        private static readonly WinTaskbarUiaProbe _instance = new WinTaskbarUiaProbe();
        public static WinTaskbarUiaProbe Instance => _instance;


        // === constructor ===

        private WinTaskbarUiaProbe() { }


        // === public api ===

        // cached or fresh; a cached null stays, so a failing explorer.exe is not retried on every call
        public WinTaskbarUiaSnapshot? Probe(IntPtr taskbarHwnd)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(taskbarHwnd, out var cached) &&
                    (DateTime.UtcNow - cached.QueriedAt).TotalMilliseconds < CacheDurationMs)
                {
                    return cached.Snapshot;
                }
            }

            var snapshot = RunWithTimeout(() => ProbeNow(taskbarHwnd));

            lock (_lock)
            {
                _cache[taskbarHwnd] = (snapshot, DateTime.UtcNow);
            }

            return snapshot;
        }


        // === private helpers ===

        // UIA is synchronous COM without cancellation; on a hung explorer.exe the worker stays
        // blocked and this stops waiting
        private static WinTaskbarUiaSnapshot? RunWithTimeout(Func<WinTaskbarUiaSnapshot?> query)
        {
            var task = Task.Run(query);
            return task.Wait(QueryTimeoutMs) ? task.Result : null;
        }

        // catches everything, the probe is optional
        private WinTaskbarUiaSnapshot? ProbeNow(IntPtr taskbarHwnd)
        {
            try
            {
                var automation = GetOrCreateAutomation();
                var root = automation.ElementFromHandle(taskbarHwnd);
                if (root == null) return null;

                return new WinTaskbarUiaSnapshot(
                    Frame: ToUiaElement(FindDescendant(automation, root, UIA_PropertyIds.UIA_AutomationIdPropertyId, TaskbarFrameAutomationId)),
                    Tray: ToUiaElement(FindDescendant(automation, root, UIA_PropertyIds.UIA_ClassNamePropertyId, TrayClassName)),
                    WidgetsButton: ToUiaElement(FindDescendant(automation, root, UIA_PropertyIds.UIA_AutomationIdPropertyId, WidgetsButtonAutomationId))
                );
            }
            catch (Exception)
            {
                return null;
            }
        }

        private IUIAutomation2 GetOrCreateAutomation()
        {
            if (_automation == null)
            {
                var automation = new CUIAutomation8();
                automation.ConnectionTimeout = (uint)QueryTimeoutMs;
                automation.TransactionTimeout = (uint)QueryTimeoutMs;
                _automation = automation;
            }

            return _automation;
        }

        // the full subtree, not only direct children
        private static IUIAutomationElement? FindDescendant(IUIAutomation2 automation, IUIAutomationElement root, int propertyId, string value)
        {
            var condition = automation.CreatePropertyCondition(propertyId, value);
            return root.FindFirst(TreeScope.TreeScope_Descendants, condition);
        }

        private static WinTaskbarUiaElement? ToUiaElement(IUIAutomationElement? element)
        {
            if (element == null) return null;

            var rect = element.CurrentBoundingRectangle;

            return new WinTaskbarUiaElement(
                BoundingRectangle: new RectInt32(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top),
                ClassName: element.CurrentClassName ?? string.Empty,
                AutomationId: element.CurrentAutomationId ?? string.Empty
            );
        }
    }
}

