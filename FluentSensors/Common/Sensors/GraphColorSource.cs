namespace FluentSensors.Common.Sensors
{
    // where a graph takes its line colour from, chosen per surface in the settings
    //
    // Hardware falls back to Accent for a sensor whose category never resolved (HardwareGroupKind.Other, e.g. a
    // motherboard or a fan controller); a grey line reads as a broken lookup rather than as a category
    public enum GraphColorSource
    {
        Accent, // the live Windows accent colour
        Custom, // the colour picked next to the selector
        Hardware // the category colour of the hardware the sensor belongs to
    }
}
