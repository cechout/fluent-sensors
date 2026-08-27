using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    // one UIA element found inside a taskbar (the frame itself, the tray, or the widgets button);
    // These have no window handle of their own, GetWindowRect cannot see them at all, this is the only way to
    // get their bounds
    public record WinTaskbarUiaElement(
        RectInt32 BoundingRectangle,
        string ClassName,
        string AutomationId
    );

    // result of one WinTaskbarUiaProbe.Probe() call for a single taskbar
    // every field is independently nullable, one element failing to resolve (e.g. Widgets disabled by the user)
    // never blocks the other two
    public record WinTaskbarUiaSnapshot(
        WinTaskbarUiaElement? Frame,
        WinTaskbarUiaElement? Tray,
        WinTaskbarUiaElement? WidgetsButton
    );
}
