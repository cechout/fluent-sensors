using FluentSensors.Core.Taskbar;
using FluentSensors.Features.TaskbarWidget;
using Windows.Graphics;

int checks = 0;
void Check(bool condition, string scenario)
{
    if (!condition) throw new Exception(scenario);
    checks++;
}

foreach (var edge in Enum.GetValues<ScreenEdge>())
foreach (uint dpi in new uint[] { 96, 144, 192 })
{
    bool vertical = edge is ScreenEdge.Left or ScreenEdge.Right;
    int scale = (int)dpi;
    int origin = -1920;
    var bar = vertical ? new RectInt32(0, origin, 48 * scale / 96, 1920)
        : new RectInt32(origin, 0, 1920, 48 * scale / 96);
    var taskbar = new WinTaskbarInfo(new IntPtr(1), bar, edge, dpi, false, IntPtr.Zero);
    RectInt32 Span(int start, int length) => vertical
        ? new RectInt32(bar.X, origin + start, bar.Width, length)
        : new RectInt32(origin + start, bar.Y, length, bar.Height);
    int Along(RectInt32 r) => vertical ? r.Y : r.X;
    int Length(RectInt32 r) => vertical ? r.Height : r.Width;
    int desired = 249 * scale / 96; // sensor slots plus divider
    int gap = 8 * scale / 96;
    var buttons = Span(400, 600);
    var tray = Span(1700, 220);
    RectInt32 Place(RectInt32? notificationArea, RectInt32? tasks) =>
        TaskbarWidgetPlacement.CalculateNearTray(taskbar, notificationArea, tasks, desired, 2, 2, gap);
    var first = Place(tray, buttons);
    Check(Length(first) == desired && Along(first) + desired == origin + 1700, $"adjacency {edge}/{dpi}");
    var moved = Place(Span(1600, 320), buttons);
    Check(Along(moved) == Along(first) - 100, $"tray expansion {edge}/{dpi}");
    Check(Length(Place(tray, Span(400, 1300))) == 0, $"no button overlap {edge}/{dpi}");
    Check(Length(Place(tray, buttons)) == desired, $"room returns {edge}/{dpi}");
    Check(Length(Place(null, buttons)) == 0, $"missing tray {edge}/{dpi}");
    Check(Length(Place(tray, null)) == 0, $"unknown buttons after restart {edge}/{dpi}");
    Check(Length(Place(Span(2000, 200), buttons)) == 0, $"offscreen tray {edge}/{dpi}");
    var wrongBand = vertical ? new RectInt32(1000, tray.Y, 48, tray.Height)
        : new RectInt32(tray.X, 1000, tray.Width, 48);
    Check(Length(Place(wrongBand, buttons)) == 0, $"stale monitor {edge}/{dpi}");
    var manual = TaskbarWidgetPlacement.Calculate(taskbar, TaskbarAnchor.Start, 35, desired, 2, 2);
    Check(Along(manual) == origin + 35, $"manual position unchanged {edge}/{dpi}");
}
Console.WriteLine($"PASS: {checks} taskbar geometry checks across four edges, three DPIs and negative monitor origins.");
