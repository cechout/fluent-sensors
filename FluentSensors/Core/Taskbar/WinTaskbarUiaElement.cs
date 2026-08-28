using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    // UIA element found inside a taskbar
    public record WinTaskbarUiaElement(
        RectInt32 BoundingRectangle,
        string ClassName,
        string AutomationId
    );

    // snapshot of UIA query result for a single taskbar
    public record WinTaskbarUiaSnapshot(
        WinTaskbarUiaElement? Frame,
        WinTaskbarUiaElement? Tray,
        WinTaskbarUiaElement? WidgetsButton
    );
}
