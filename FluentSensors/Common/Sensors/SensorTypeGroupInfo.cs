using FluentSensors.Common.UI;


namespace FluentSensors.Common.Sensors
{
    public enum SensorTypeGroupKind
    {
        Temperature,
        Electrical,
        Load,
        Level,
        Clock,
        Cooling,
        Data,
        Throughput,
        Neutral // no colour of its own
    }

    // the sensor type groups:
    // maps a raw LHM SensorType to a group by what the value says (how hot, how much power, how busy, how full), and
    // the group to its colour; the counterpart of HardwareGroupInfo for GraphColorSource.SensorType
    public static class SensorTypeGroupInfo
    {
        // the LHM sensor that says how full a drive is; LHM files it under Load
        private const string UsedSpaceName = "Used Space";

        // the raw SensorType.ToString() ("Clock", "Load"); the sensor name only matters for the one sensor whose type
        // says something else than its value
        // Load is how busy something is, Level how full or how far up: a charge, a capacity, a signal, a brightness
        public static SensorTypeGroupKind GetKind(string sensorType, string sensorName = null)
        {
            return sensorType switch
            {
                "Temperature" => SensorTypeGroupKind.Temperature,
                "Voltage" or "Current" or "Power" => SensorTypeGroupKind.Electrical,
                "Load" when sensorName == UsedSpaceName => SensorTypeGroupKind.Level,
                "Load" => SensorTypeGroupKind.Load,
                "Level" or "Energy" or "Signal" => SensorTypeGroupKind.Level, // Energy is a battery capacity, Signal the RSSI
                "Clock" or "Frequency" or "Factor" or "Timing" => SensorTypeGroupKind.Clock,
                "Fan" or "Control" or "Flow" or "Noise" => SensorTypeGroupKind.Cooling, // Control is the fan duty
                "Data" or "SmallData" => SensorTypeGroupKind.Data,
                "Throughput" => SensorTypeGroupKind.Throughput,
                _ => SensorTypeGroupKind.Neutral // TimeSpan, Conductivity, Humidity
            };
        }

        // false for the neutral group, which resolves like sensor type colours off
        // close in perceived lightness, so no group stands out; both themes carry the same value
        // amber, green, violet and blue sit a step lighter, so the groups on one hardware view tell apart
        public static bool TryGetColors(string sensorType, string sensorName, out ThemeColorPair colors)
        {
            switch (GetKind(sensorType, sensorName))
            {
                case SensorTypeGroupKind.Temperature: colors = ThemeColorPair.FromRgb(0xEC785B, 0xEC785B); return true;
                case SensorTypeGroupKind.Electrical: colors = ThemeColorPair.FromRgb(0xE7AC2A, 0xE7AC2A); return true;
                case SensorTypeGroupKind.Load: colors = ThemeColorPair.FromRgb(0x58A8FE, 0x58A8FE); return true;
                // a lime in the widest hue gap of the palette, between the amber and the green
                case SensorTypeGroupKind.Level: colors = ThemeColorPair.FromRgb(0xA9BD40, 0xA9BD40); return true;
                case SensorTypeGroupKind.Clock: colors = ThemeColorPair.FromRgb(0xBA93FB, 0xBA93FB); return true;
                case SensorTypeGroupKind.Cooling: colors = ThemeColorPair.FromRgb(0x05B3C1, 0x05B3C1); return true;
                case SensorTypeGroupKind.Data: colors = ThemeColorPair.FromRgb(0x4FCE74, 0x4FCE74); return true;
                case SensorTypeGroupKind.Throughput: colors = ThemeColorPair.FromRgb(0xE274AB, 0xE274AB); return true;
                default: colors = default; return false;
            }
        }
    }
}
