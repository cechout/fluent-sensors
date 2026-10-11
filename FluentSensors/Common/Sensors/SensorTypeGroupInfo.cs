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
        // every value has the same perceived lightness, so no group stands out; both themes carry the same value
        public static bool TryGetColors(string sensorType, out ThemeColorPair colors)
        {
            switch (GetKind(sensorType))
            {
                case SensorTypeGroupKind.Temperature: colors = ThemeColorPair.FromRgb(0xEC785B, 0xEC785B); return true;
                case SensorTypeGroupKind.Electrical: colors = ThemeColorPair.FromRgb(0xCD9200, 0xCD9200); return true;
                case SensorTypeGroupKind.Load: colors = ThemeColorPair.FromRgb(0x51A2F8, 0x51A2F8); return true;
                case SensorTypeGroupKind.Clock: colors = ThemeColorPair.FromRgb(0xAD87ED, 0xAD87ED); return true;
                case SensorTypeGroupKind.Cooling: colors = ThemeColorPair.FromRgb(0x05B3C1, 0x05B3C1); return true;
                case SensorTypeGroupKind.Data: colors = ThemeColorPair.FromRgb(0x52B766, 0x52B766); return true;
                case SensorTypeGroupKind.Throughput: colors = ThemeColorPair.FromRgb(0xE274AB, 0xE274AB); return true;
                default: colors = default; return false;
            }
        }
    }
}
