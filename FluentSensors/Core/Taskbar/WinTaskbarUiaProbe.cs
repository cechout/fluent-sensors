using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UIA;
using UIAutomationClient;
using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    // cross-process UIA lookups for taskbar sub-elements that have no window handle of their own (the taskbar
    // frame root, the system tray, the widgets button);
    // GetWindowRect/SHAppBarMessage in WinTaskbarService only know the taskbar windows own bounds, not what
    // explorer.exe actually renders inside it
    // every query can return null at any time; this is optional enrichment only, taskbar placement logic must
    // keep working with this returning null
    public class WinTaskbarUiaProbe
    {
        // === fields ===

        // a single UIA round trip to an unresponsive explorer.exe should never block the caller longer than this;
        // also set as the native ConnectionTimeout/TransactionTimeout on the automation object itself
        private const int QueryTimeoutMs = 500;

        // taskbar UI structure basically never changes while explorer.exe keeps running, no need to re-query more often
        private const int CacheDurationMs = 5000;

        // KNOWN UNRELIABLE:
        // these class names and automation IDs have no official Microsoft documentation; they are internal
        // implementation details of explorer.exes XAML Islands taskbar UI and can shift between Windows 11 builds
        // confirmed against live UIA tree dumps on real Windows 11 hardware (WinTaskbarDebugDump.DumpTaskbarTree)
        // TaskbarFrame carries a real AutomationId, TrayNotifyWnd is a classic Win32 child class name
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

        // returns cached or fresh snapshot; cached null is returned as-is to avoid continuous slow cross-process retries
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

        // background-thread timeout wrapper:
        // UIA calls are synchronous COM with no cancellation support; if explorer.exe hangs, the worker thread
        // stays blocked inside the call, so this stops waiting instead of trying to cancel it
        private static WinTaskbarUiaSnapshot? RunWithTimeout(Func<WinTaskbarUiaSnapshot?> query)
        {
            var task = Task.Run(query);
            return task.Wait(QueryTimeoutMs) ? task.Result : null;
        }

        // catches all exceptions; this probe is optional enrichment only
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

        // full subtree search instead of just direct children
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

