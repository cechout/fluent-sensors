using Windows.Graphics;


namespace FluentSensors.Core.Taskbar
{
    // one UIA element of a taskbar
    public record WinTaskbarUiaElement(
        RectInt32 BoundingRectangle, // screen coordinates
        string ClassName, // TaskbarFrame, SystemTray
        string AutomationId // when Windows assigns one
    );

    // the UIA result of one taskbar
    public record WinTaskbarUiaSnapshot(
        WinTaskbarUiaElement? Frame,
        WinTaskbarUiaElement? Tray, // the notification area
        WinTaskbarUiaElement? WidgetsButton
    );
}

