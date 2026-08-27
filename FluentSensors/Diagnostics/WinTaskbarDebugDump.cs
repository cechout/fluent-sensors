using System;

using UIAutomationClient;

using Windows.Graphics;

using FluentSensors.Core.Taskbar;


namespace FluentSensors.Diagnostics
{
    // developer diagnostic tool (not part of any feature):
    // dumps everything the taskbar detection backend (WinTaskbarService, WinTaskbarUiaProbe, WinShellStateWatcher)
    // currently sees to the Debug output window, to verify the native/UIA queries actually return plausible data
    // before Phase 3 invests any time in an actual widget window
    // directly answers the two open Phase 2 questions: whether UIA gets through at all from this elevated process,
    // and whether the TaskbarFrame/SystemTrayFrame/WidgetsButton identifiers still match on this Windows build
    //
    // Call this manually from anywhere (e.g. MainWindow constructor, wrapped in Task.Run since WinTaskbarUiaProbe
    // blocks on cross-process COM calls) whenever the taskbar backend needs re-checking
    public static class WinTaskbarDebugDump
    {
        public static void Dump()
        {
            System.Diagnostics.Debug.WriteLine("========== WinTaskbarService Dump ==========");

            var taskbars = WinTaskbarService.Instance.DiscoverNow();
            System.Diagnostics.Debug.WriteLine($"Taskbars found: {taskbars.Count}");

            foreach (var taskbar in taskbars)
            {
                System.Diagnostics.Debug.WriteLine($"--- Taskbar hwnd=0x{taskbar.Hwnd:X} ---");
                System.Diagnostics.Debug.WriteLine(
                    $"  GetWindowRect: X={taskbar.Rect.X} Y={taskbar.Rect.Y} W={taskbar.Rect.Width} H={taskbar.Rect.Height}");
                System.Diagnostics.Debug.WriteLine(
                    $"  Edge={taskbar.Edge} | Dpi={taskbar.Dpi} | IsAutoHide={taskbar.IsAutoHide} | Monitor=0x{taskbar.Monitor:X}");

                var uia = WinTaskbarUiaProbe.Instance.Probe(taskbar.Hwnd);
                if (uia == null)
                {
                    // open point 1: couldnt even reach the root element, either UIPI blocked the elevated-process
                    // call or the query timed out
                    System.Diagnostics.Debug.WriteLine("  UIA: root element unreachable (timeout, or UIPI blocked the elevated process, see open point 1)");
                    continue;
                }

                System.Diagnostics.Debug.WriteLine("  UIA: root element reached (open point 1 answered: yes, UIA gets through)");
                DumpUiaElement("Frame", uia.Frame, taskbar.Rect);
                DumpUiaElement("Tray", uia.Tray, taskbar.Rect);
                DumpUiaElement("WidgetsButton", uia.WidgetsButton, taskbar.Rect);

                // none of the three targeted lookups matched: dump the actual live tree instead of guessing again
                // from the same outdated community sources, this shows the real ClassName/AutomationId values on
                // this exact Windows build (open point 2)
                if (uia.Frame == null && uia.Tray == null && uia.WidgetsButton == null)
                {
                    DumpTaskbarTree(taskbar.Hwnd);
                }
            }

            System.Diagnostics.Debug.WriteLine("--- WinShellStateWatcher ---");
            System.Diagnostics.Debug.WriteLine($"IsFullscreenAppActive={WinShellStateWatcher.Instance.IsFullscreenAppActive()}");

            System.Diagnostics.Debug.WriteLine("========== End Dump ==========");
        }

        // a missing element here is open point 2: the ClassName/AutomationId may have shifted on this Windows
        // build, see the KNOWN UNRELIABLE note in WinTaskbarUiaProbe
        private static void DumpUiaElement(string label, WinTaskbarUiaElement? element, RectInt32 taskbarRect)
        {
            if (element == null)
            {
                System.Diagnostics.Debug.WriteLine($"  UIA {label}: not found (see open point 2)");
                return;
            }

            var rect = element.BoundingRectangle;

            // the "invisible border" the plan wants to see: how far the UIA bounds sit inside the raw
            // GetWindowRect bounds on all four sides
            int leftInset = rect.X - taskbarRect.X;
            int topInset = rect.Y - taskbarRect.Y;
            int rightInset = (taskbarRect.X + taskbarRect.Width) - (rect.X + rect.Width);
            int bottomInset = (taskbarRect.Y + taskbarRect.Height) - (rect.Y + rect.Height);

            System.Diagnostics.Debug.WriteLine($"  UIA {label}: ClassName='{element.ClassName}' AutomationId='{element.AutomationId}'");
            System.Diagnostics.Debug.WriteLine($"    Rect: X={rect.X} Y={rect.Y} W={rect.Width} H={rect.Height}");
            System.Diagnostics.Debug.WriteLine(
                $"    Inset vs GetWindowRect: Left={leftInset} Top={topInset} Right={rightInset} Bottom={bottomInset}");
        }

        // one-off diagnostic pass, gets its own CUIAutomation8 instance rather than reusing WinTaskbarUiaProbes:
        // that one is scoped to exactly three targeted lookups with cache+timeout, this is a much wider,
        // occasional exploratory walk, mixing the two concerns into one class would blur both
        // 5 levels down is comfortably past where TaskbarFrame sits (2 levels under the taskbar hwnd itself per
        // community findings), with room to spare for whatever this Windows build actually does
        private static void DumpTaskbarTree(IntPtr taskbarHwnd)
        {
            System.Diagnostics.Debug.WriteLine("  --- raw UIA tree (ClassName / AutomationId / Name), up to 5 levels ---");

            try
            {
                var automation = new CUIAutomation8();
                var root = automation.ElementFromHandle(taskbarHwnd);
                if (root == null)
                {
                    System.Diagnostics.Debug.WriteLine("  (root element unreachable)");
                    return;
                }

                DumpElementRecursive(automation, root, depth: 0, maxDepth: 5);
            }
            catch (Exception ex)
            {
                // diagnostic-only, never worth taking the whole dump down over
                System.Diagnostics.Debug.WriteLine($"  (tree dump failed: {ex.Message})");
            }
        }

        private static void DumpElementRecursive(IUIAutomation2 automation, IUIAutomationElement element, int depth, int maxDepth)
        {
            string indent = new string(' ', 2 + depth * 2);
            System.Diagnostics.Debug.WriteLine(
                $"  {indent}ClassName='{element.CurrentClassName}' AutomationId='{element.CurrentAutomationId}' Name='{element.CurrentName}'");

            if (depth >= maxDepth) return;

            var children = element.FindAll(TreeScope.TreeScope_Children, automation.CreateTrueCondition());

            // safety cap, taskbar trees shouldnt realistically be this wide, just guarding against flooding the
            // output if something unexpected does show up
            int count = Math.Min(children.Length, 40);
            for (int i = 0; i < count; i++)
            {
                DumpElementRecursive(automation, children.GetElement(i), depth + 1, maxDepth);
            }
        }
    }
}
