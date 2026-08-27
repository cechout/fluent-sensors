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
    // every query can return null at any time (element not found, explorer.exe unresponsive, identifiers changed
    // on this Windows build); this is optional enrichment only, all taskbar placement logic must keep working
    // with this returning null forever
    public class WinTaskbarUiaProbe
    {
        // === fields ===

        // a single UIA round trip to an unresponsive explorer.exe should never block the caller longer than this;
        // also set as the native ConnectionTimeout/TransactionTimeout on the automation object itself
        private const int QueryTimeoutMs = 500;

        // taskbar UI structure basically never changes while explorer.exe keeps running, no need to re-query more
        // often than this
        private const int CacheDurationMs = 5000;

        // KNOWN UNRELIABLE:
        // these class names and automation ids have no official Microsoft documentation, they are internal
        // implementation details of explorer.exes XAML Islands taskbar UI and are known to shift between Windows
        // 11 builds
        // confirmed against a live UIA tree dump on a real Windows 11 machine (WinTaskbarDebugDump.DumpTaskbarTree)
        // initial guesses came from community reverse engineering and turned out wrong for two of the three:
        // TaskbarFrame actually carries a real AutomationId, not just a ClassName, and there is no
        // SystemTray.SystemTrayFrame in the live tree at all, the tray sits in a differently named classic Win32
        // child window instead
        // community sources for background, not the actual source of truth here:
        // https://github.com/ramensoftware/windows-11-taskbar-styling-guide
        // https://github.com/ramensoftware/windhawk-mods/discussions/679
        // WidgetsButton was not observed in the tree at all (Widgets was disabled on the test machine), the
        // automation id here is still unconfirmed but has no counter-evidence either
        // a null Frame/Tray/WidgetsButton on the debug dump most likely means the identifier changed again on
        // this Windows build, not that something is broken
        private const string TaskbarFrameAutomationId = "TaskbarFrame";
        private const string TrayClassName = "TrayNotifyWnd";
        private const string WidgetsButtonAutomationId = "WidgetsButton";

        private readonly object _lock = new();
        private readonly Dictionary<IntPtr, (WinTaskbarUiaSnapshot? Snapshot, DateTime QueriedAt)> _cache = new();

        private IUIAutomation2? _automation; // created once, reused for every query


        // === singleton instance ===

        private static readonly WinTaskbarUiaProbe _instance = new WinTaskbarUiaProbe();
        public static WinTaskbarUiaProbe Instance => _instance;


        // === constructor ===

        private WinTaskbarUiaProbe() { }


        // === public api ===

        // never throws; a cached null is returned as-is, so a taskbar whose identifiers dont resolve on this
        // Windows build does not retry a slow cross-process query on every single call
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
        // UIA calls are synchronous COM with no cancellation support, if explorer.exe is genuinely hung the
        // worker thread stays blocked forever inside the call, this just stops waiting on it here instead of
        // trying to cancel it;
        // a permanently-hung worker thread is a rare, cheap enough cost against never blocking the caller
        private static WinTaskbarUiaSnapshot? RunWithTimeout(Func<WinTaskbarUiaSnapshot?> query)
        {
            var task = Task.Run(query);
            return task.Wait(QueryTimeoutMs) ? task.Result : null;
        }

        // catches everything, not just COMException
        // (this probe is optional enrichment only, every caller must keep working on null regardless of why a
        // particular query failed)
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

                // an unresponsive explorer.exe should time out instead of hanging every future call made through
                // this same automation instance
                automation.ConnectionTimeout = (uint)QueryTimeoutMs;
                automation.TransactionTimeout = (uint)QueryTimeoutMs;

                _automation = automation;
            }

            return _automation;
        }

        // full subtree search instead of just direct children
        // (the exact tree depth to Frame/Tray/WidgetsButton has shifted between Windows builds before)
        private static IUIAutomationElement? FindDescendant(IUIAutomation2 automation, IUIAutomationElement root, int propertyId, string value)
        {
            var condition = automation.CreatePropertyCondition(propertyId, value);
            return root.FindFirst(TreeScope.TreeScope_Descendants, condition);
        }

        private static WinTaskbarUiaElement? ToUiaElement(IUIAutomationElement? element)
        {
            if (element == null) return null;

            // tagRECT from the UIA typelib, left/top/right/bottom exactly like the native RECT struct
            var rect = element.CurrentBoundingRectangle;

            return new WinTaskbarUiaElement(
                BoundingRectangle: new RectInt32(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top),
                ClassName: element.CurrentClassName ?? string.Empty,
                AutomationId: element.CurrentAutomationId ?? string.Empty
            );
        }
    }
}
