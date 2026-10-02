namespace FluentSensors.Common.Sensors
{
    // the line colour source, per surface in the settings
    // Hardware falls back to Accent for HardwareGroupKind.Other (a motherboard, a fan controller); grey
    // would read as a broken lookup
    public enum GraphColorSource
    {
        Accent, // the live Windows accent colour
        Custom, // picked next to the selector
        Hardware // the category colour
    }
}
