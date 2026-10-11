namespace FluentSensors.Common.Sensors
{
    // when a graph with a hardware colour of its own (the performance page) shows the sensor type colour instead
    public enum SensorTypeColorRule
    {
        BySetting, // while the sensor type colours switch is on
        Always,
        Never
    }
}
