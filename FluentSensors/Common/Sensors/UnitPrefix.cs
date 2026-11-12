namespace FluentSensors.Common.Sensors
{
    // the unit a scaled sensor type is shown in; Auto picks it per value by its size
    public enum UnitPrefix
    {
        Auto,
        Milli, // mW, mA
        None, // W, A
        Kilo, // KB, kbit, KB/s
        Mega, // MB, Mbit, MB/s, MHz
        Giga // GB, Gbit, GB/s, GHz
    }
}
