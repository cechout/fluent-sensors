using FluentSensors.Common.UI;


namespace FluentSensors.Common.Sensors
{
    public enum SensorTypeGroupKind
    {
        Temperature,
        Electrical,
        Load,
        Clock,
        Cooling,
        Data,
        Throughput,
        Neutral // no colour of its own
    }

    // the sensor type groups:
    // maps a raw LHM SensorType to a group by what the value says (how hot, how much power, how busy), and the
    // group to its colour; the counterpart of HardwareGroupInfo for GraphColorSource.SensorType
    public static class SensorTypeGroupInfo
    {
        // the raw SensorType.ToString() ("Clock", "Load")
        public static SensorTypeGroupKind GetKind(string sensorType)
        {
            return sensorType switch
            {
                "Temperature" => SensorTypeGroupKind.Temperature,
                "Voltage" or "Current" or "Power" or "Energy" => SensorTypeGroupKind.Electrical,
                "Load" or "Level" => SensorTypeGroupKind.Load,
                "Clock" or "Frequency" or "Factor" or "Timing" => SensorTypeGroupKind.Clock,
                "Fan" or "Control" or "Flow" or "Noise" => SensorTypeGroupKind.Cooling, // Control is the fan duty
                "Data" or "SmallData" => SensorTypeGroupKind.Data,
                "Throughput" => SensorTypeGroupKind.Throughput,
                _ => SensorTypeGroupKind.Neutral // TimeSpan, Conductivity, Humidity
            };
        }

        // false for the neutral group, which resolves like sensor type colours off
        // every dark value has the same perceived lightness, and every light value, so no group stands out
        public static bool TryGetColors(string sensorType, out ThemeColorPair colors)
        {
            switch (GetKind(sensorType))
            {
                case SensorTypeGroupKind.Temperature: colors = ThemeColorPair.FromRgb(0xFC8E72, 0xB05139); return true;
                case SensorTypeGroupKind.Electrical: colors = ThemeColorPair.FromRgb(0xDFA635, 0x946900); return true;
                case SensorTypeGroupKind.Load: colors = ThemeColorPair.FromRgb(0x73B5FF, 0x2F74BB); return true;
                case SensorTypeGroupKind.Clock: colors = ThemeColorPair.FromRgb(0xBF9BFC, 0x7E5DB1); return true;
                case SensorTypeGroupKind.Cooling: colors = ThemeColorPair.FromRgb(0x08C8D8, 0x05818B); return true;
                case SensorTypeGroupKind.Data: colors = ThemeColorPair.FromRgb(0x6DC97D, 0x2F8543); return true;
                case SensorTypeGroupKind.Throughput: colors = ThemeColorPair.FromRgb(0xF38ABE, 0xA84E7C); return true;
                default: colors = default; return false;
            }
        }
    }
}
