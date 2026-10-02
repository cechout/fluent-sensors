namespace FluentSensors.Common.Sensors
{
    // the sensor units:
    // an LHM SensorType to its display unit, one source for every graph, row, threshold and recording
    public static class SensorUnitFormatter
    {
        // Clock, SmallData and Throughput step up a unit here
        private const double ScaleThreshold = 1000;

        // the sensors report binary units (SmallData MiB, Data GiB, Throughput MiB/s via
        // HardwareMonitorService), shown as MB and GB
        // bits are decimal like advertised network speeds, so these convert to decimal megabits and gigabits
        private const double MebibytesToMegabits = 1_048_576.0 * 8 / 1_000_000;
        private const double GibibytesToGigabits = 1_073_741_824.0 * 8 / 1_000_000_000;

        // the live switches; SettingsService.DataSizeUnitBasis and DataSpeedUnitBasis persist and mirror them in here
        // size is SmallData and Data, speed is Throughput
        public static DataUnitBasis DataSizeBasis { get; set; } = DataUnitBasis.Byte;
        public static DataUnitBasis DataSpeedBasis { get; set; } = DataUnitBasis.Byte;

        public static string GetUnit(string sensorType)
        {
            return sensorType switch
            {
                "SmallData" when DataSizeBasis == DataUnitBasis.Bit => "Mbit",
                "Data" when DataSizeBasis == DataUnitBasis.Bit => "Gbit",
                "Throughput" when DataSpeedBasis == DataUnitBasis.Bit => "Mbit/s",
                _ => GetRawUnit(sensorType)
            };
        }

        // the stored unit regardless of the settings, for unscaled values like the csv recording
        public static string GetRawUnit(string sensorType)
        {
            return sensorType switch
            {
                "Temperature" => "°C",
                "Power" => "W",
                "Load" => "%",
                "Clock" => "MHz",
                "SmallData" => "MB",
                "Data" => "GB",
                "Voltage" => "V",
                "Fan" => "RPM",
                "Throughput" => "MB/s",
                _ => ""
            };
        }

        // a raw value in its display unit, scaled at ScaleThreshold (MHz to GHz, MB to GB, MB/s to GB/s); callers
        // wanting the bare number come here too, the decision lives only here
        public static (double Value, string Unit) Scale(double value, string sensorType)
        {
            return sensorType switch
            {
                "Clock" when value >= ScaleThreshold => (value / ScaleThreshold, "GHz"),
                "SmallData" when DataSizeBasis == DataUnitBasis.Bit => ScaleBits(value * MebibytesToMegabits, "Mbit", "Gbit"),
                "SmallData" when value >= ScaleThreshold => (value / ScaleThreshold, "GB"),
                "Data" when DataSizeBasis == DataUnitBasis.Bit => (value * GibibytesToGigabits, "Gbit"),
                "Throughput" when DataSpeedBasis == DataUnitBasis.Bit => ScaleBits(value * MebibytesToMegabits, "Mbit/s", "Gbit/s"),
                "Throughput" when value >= ScaleThreshold => (value / ScaleThreshold, "GB/s"),
                _ => (value, GetUnit(sensorType))
            };
        }

        public static string Format(double value, string sensorType)
        {
            var (scaledValue, unit) = Scale(value, sensorType);
            return $"{scaledValue:F1} {unit}";
        }

        // the way back, the displayed base unit (MB, Mbit, MB/s, Mbit/s) to the raw one, so SensorTypeProfiles steps
        // stay round in bytes and bits
        public static double ToRawValue(double displayValue, string sensorType)
        {
            return sensorType switch
            {
                "SmallData" when DataSizeBasis == DataUnitBasis.Bit => displayValue / MebibytesToMegabits,
                "Data" when DataSizeBasis == DataUnitBasis.Bit => displayValue / GibibytesToGigabits,
                "Throughput" when DataSpeedBasis == DataUnitBasis.Bit => displayValue / MebibytesToMegabits,
                _ => displayValue
            };
        }

        // the bit side, the same threshold
        private static (double Value, string Unit) ScaleBits(double megabits, string megabitUnit, string gigabitUnit)
        {
            return megabits >= ScaleThreshold
                ? (megabits / ScaleThreshold, gigabitUnit)
                : (megabits, megabitUnit);
        }
    }
}
