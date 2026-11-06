using FluentSensors.Core.Taskbar;
using Windows.Graphics;

// Read-only integration check; build the application first for its COM interop assemblies.
var hwnd = NativeMethods.FindWindowExW(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var r))
    throw new Exception("No taskbar is available for the live check.");
var rect = new RectInt32(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
var bar = new WinTaskbarInfo(hwnd, rect, rect.Height > rect.Width ? ScreenEdge.Left : ScreenEdge.Bottom,
    NativeMethods.GetDpiForWindow(hwnd), false, IntPtr.Zero);
var tracker = new WinTaskbarTrayTracker();
var watch = System.Diagnostics.Stopwatch.StartNew();
var snapshot = tracker.Read(bar);
for (int attempt = 0; snapshot.Buttons == null && attempt < 30; attempt++)
{
    await Task.Delay(200);
    snapshot = tracker.Read(bar);
}
if (snapshot.Tray == null || snapshot.Buttons == null)
    throw new Exception("The live taskbar did not expose both tray and button landmarks.");
Console.WriteLine($"PASS: live tray and task-button detection in {watch.ElapsedMilliseconds} ms.");
var tray = snapshot.Tray.Value;
var buttons = snapshot.Buttons.Value;
Console.WriteLine($"Tray: ({tray.X}, {tray.Y}, {tray.Width}, {tray.Height}); buttons: ({buttons.X}, {buttons.Y}, {buttons.Width}, {buttons.Height}); DPI: {bar.Dpi}.");
if (args.Contains("--verify-widget"))
{
    var processIds = System.Diagnostics.Process.GetProcessesByName("FluentSensors").Select(p => p.Id).ToHashSet();
    bool matched = false;
    for (var child = NativeMethods.FindWindowExW(hwnd, IntPtr.Zero, null, null); child != IntPtr.Zero;
        child = NativeMethods.FindWindowExW(hwnd, child, null, null))
    {
        NativeMethods.GetWindowThreadProcessId(child, out uint owner);
        if (!processIds.Contains((int)owner) || !NativeMethods.GetWindowRect(child, out var widget)) continue;
        bool vertical = bar.IsVertical;
        int end = vertical ? widget.Bottom : widget.Right;
        int start = vertical ? tray.Y : tray.X;
        int size = vertical ? widget.Bottom - widget.Top : widget.Right - widget.Left;
        if (size > 0 && end == start)
        {
            matched = true;
            Console.WriteLine($"PASS: running widget ({widget.Left}, {widget.Top}, {widget.Right - widget.Left}, {widget.Bottom - widget.Top}) ends exactly at the live tray boundary.");
        }
    }
    if (!matched) throw new Exception("No visible Fluent Sensors child was adjacent to the tray. Enable the option and open its taskbar widget for this check.");
}
tracker.Reset();
var reset = tracker.Read(bar);
if (reset.Buttons != null) throw new Exception("Reset retained cached button bounds.");
Console.WriteLine("PASS: toggling or reopening clears prior button bounds before a fresh scan.");
var invalid = tracker.Read(bar with { Hwnd = IntPtr.Zero });
if (invalid.Tray != null || invalid.Buttons != null)
    throw new Exception("A changed taskbar handle retained stale landmarks.");
Console.WriteLine("PASS: taskbar-handle change clears cached landmarks.");
