using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    // UIA element found inside a taskbar
    public record WinTaskbarUiaElement(
        RectInt32 BoundingRectangle, // element bounding rectangle in screen coordinates
        string ClassName, // native UI element class name (e.g. TaskbarFrame, SystemTray)
        string AutomationId // automation identifier if assigned by Windows
    );

    // snapshot of UIA query result for a single taskbar
    public record WinTaskbarUiaSnapshot(
        WinTaskbarUiaElement? Frame, // taskbar frame container
        WinTaskbarUiaElement? Tray, // system tray notification area
        WinTaskbarUiaElement? WidgetsButton // widgets button element
    );
}

