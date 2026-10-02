namespace FluentSensors.Common.UI
{
    // the page after the splash; a pending sensor profile from the taskbar flyout wins (see
    // the MainWindow splash reveal)
    public enum StartupPage
    {
        Start, // update state, system snapshot, about
        Sensors,
        Performance
    }
}
