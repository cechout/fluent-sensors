namespace FluentSensors.Common.UI
{
    // which page the app lands on once the splash screen is gone
    // a pending sensor profile from the taskbar flyout outranks this, see MainWindows splash reveal
    public enum StartupPage
    {
        Start, // the overview page: update state, system snapshot, about
        Sensors,
        Performance
    }
}
