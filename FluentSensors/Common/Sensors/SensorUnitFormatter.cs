namespace FluentSensors.Common.Sensors
{
    // maps a LibreHardwareMonitor SensorType string to its display unit; single source of truth for anything
    // on the Performance page, so every graph formats its value label the same way
    public static class SensorUnitFormatter
    {
        // Clock, SmallData and Throughput switch to a bigger unit once a raw value reaches this
        private const double ScaleThreshold = 1000;

        // the sensors report binary units (SmallData in MiB, Data in GiB, Throughput normalized to MiB/s by
        // HardwareMonitorService), which the byte units have always shown as MB and GB
        // bit units are decimal instead, the way network speeds are advertised, so these turn the binary value into
        // decimal megabits and gigabits
        private const double MebibytesToMegabits = 1_048_576.0 * 8 / 1_000_000;
        private const double GibibytesToGigabits = 1_073_741_824.0 * 8 / 1_000_000_000;

        // the live switches, not the settings: SettingsService.DataSizeUnitBasis and DataSpeedUnitBasis own
        // persistence and the change event and mirror their values in here, which keeps this class out of
        // persistence
        // size covers SmallData and Data, speed covers Throughput
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

        // the unit a raw sensor value is stored in, whatever the data unit settings say; for anything that keeps
        // the unscaled value, like the csv recording
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

        // resolves a raw value to whatever unit it should actually be shown in right now, scaling it down once it
        // reaches ScaleThreshold (Clock: MHz -> GHz, SmallData: MB -> GB, Throughput: MB/s -> GB/s)
        //
        // callers that only need the bare number, without any unit text, still go through here instead of
        // re-checking the threshold themselves, so the scaling decision only exists in this one place
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

        // the way back: turns an amount in the displayed base unit (MB, Mbit, MB/s, Mbit/s, ...) into the raw unit
        // a sensor value is stored in, so the step sizes and defaults in SensorTypeProfiles stay the same round
        // numbers on screen in bytes and in bits
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

        // the bit side of the scaling above, same threshold: megabits, then gigabits
        private static (double Value, string Unit) ScaleBits(double megabits, string megabitUnit, string gigabitUnit)
        {
            return megabits >= ScaleThreshold
                ? (megabits / ScaleThreshold, gigabitUnit)
                : (megabits, megabitUnit);
        }
    }
}
