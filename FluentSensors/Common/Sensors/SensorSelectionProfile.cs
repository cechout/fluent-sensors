namespace FluentSensors.Common.Sensors
{
    // which consumer a sensor is pinned to on the SensorsPage; each has its own independent, ordered selection
    // Csv has no consumer yet, the selection exists but nothing reads it until CSV recording ships
    public enum SensorSelectionProfile
    {
        WidgetWindow,
        Csv,
        Taskbar
    }
}
