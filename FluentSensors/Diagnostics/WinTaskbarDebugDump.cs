using System;
using UIAutomationClient;
using Windows.Graphics;

using FluentSensors.Core.Taskbar;


namespace FluentSensors.Diagnostics
{
    // developer diagnostics for taskbar detection backend
    public static class WinTaskbarDebugDump
    {
        // === public api ===

        // outputs discovered taskbars, UIA element insets, and fullscreen state to debug console
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
                    System.Diagnostics.Debug.WriteLine("  UIA: root element unreachable");
                    continue;
                }

                System.Diagnostics.Debug.WriteLine("  UIA: root element reached");
                DumpUiaElement("Frame", uia.Frame, taskbar.Rect);
                DumpUiaElement("Tray", uia.Tray, taskbar.Rect);
                DumpUiaElement("WidgetsButton", uia.WidgetsButton, taskbar.Rect);

                if (uia.Frame == null && uia.Tray == null && uia.WidgetsButton == null)
                {
                    DumpTaskbarTree(taskbar.Hwnd);
                }
            }

            System.Diagnostics.Debug.WriteLine("--- WinShellStateWatcher ---");
            System.Diagnostics.Debug.WriteLine($"IsFullscreenAppActive={WinShellStateWatcher.Instance.IsFullscreenAppActive()}");

            System.Diagnostics.Debug.WriteLine("========== End Dump ==========");
        }


        // === private helpers ===

        private static void DumpUiaElement(string label, WinTaskbarUiaElement? element, RectInt32 taskbarRect)
        {
            if (element == null)
            {
                System.Diagnostics.Debug.WriteLine($"  UIA {label}: not found");
                return;
            }

            var rect = element.BoundingRectangle;
            int leftInset = rect.X - taskbarRect.X;
            int topInset = rect.Y - taskbarRect.Y;
            int rightInset = (taskbarRect.X + taskbarRect.Width) - (rect.X + rect.Width);
            int bottomInset = (taskbarRect.Y + taskbarRect.Height) - (rect.Y + rect.Height);

            System.Diagnostics.Debug.WriteLine($"  UIA {label}: ClassName='{element.ClassName}' AutomationId='{element.AutomationId}'");
            System.Diagnostics.Debug.WriteLine($"    Rect: X={rect.X} Y={rect.Y} W={rect.Width} H={rect.Height}");
            System.Diagnostics.Debug.WriteLine(
                $"    Inset vs GetWindowRect: Left={leftInset} Top={topInset} Right={rightInset} Bottom={bottomInset}");
        }

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
            int count = Math.Min(children.Length, 40);
            for (int i = 0; i < count; i++)
            {
                DumpElementRecursive(automation, children.GetElement(i), depth + 1, maxDepth);
            }
        }
    }
}

