namespace FluentSensors.Common.Sensors
{
    // the line colour source: one global setting, and one per surface that follows it or overrides it
    // Hardware falls back to Accent for HardwareGroupKind.Other (a motherboard, a fan controller); grey
    // would read as a broken lookup
    // SensorType does the same for a type without a group colour (see SensorTypeGroupInfo)
    public enum GraphColorSource
    {
        Accent, // the live Windows accent colour
        Custom, // picked next to the selector
        Hardware, // the category colour
        SensorType, // the colour of the sensor type group
        Global // a surface only; the global setting
    }
}
