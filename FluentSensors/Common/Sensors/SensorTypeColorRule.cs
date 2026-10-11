namespace FluentSensors.Common.Sensors
{
    // when a graph with a hardware colour of its own (the performance page) shows the sensor type colour instead
    public enum SensorTypeColorRule
    {
        BySetting, // while the global colour source says sensor type
        Always,
        Never
    }
}
