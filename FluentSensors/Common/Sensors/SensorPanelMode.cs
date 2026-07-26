namespace FluentSensors.Common.Sensors
{
    // controls which parts of SensorPanelControl are visible; each mode is a fixed preset rather than
    // individually toggleable flags, since these three specific combinations are the only ones currently needed
    public enum SensorPanelMode
    {
        // Standard look:
        // no separate title row, status header (name + current value) inline, control/threshold buttons toggleable via the
        // existing panel-toggle button
        Standard,

        // same as Standard, but with an additional title row above everything else
        Performance,

        // title row + graph only:
        // no status header, no control buttons, no threshold buttons, regardless of the panels own toggle state
        Minimal
    }
}