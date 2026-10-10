namespace FluentSensors.Common.UI
{
    // where a widget window sits among the other windows; the z-order button cycles through them in this order
    public enum WindowZOrder
    {
        AlwaysOnTop, // over every other app
        Normal, // an ordinary window
        Desktop // below every other window, standing through Win+D, off the taskbar and Alt+Tab
    }
}
