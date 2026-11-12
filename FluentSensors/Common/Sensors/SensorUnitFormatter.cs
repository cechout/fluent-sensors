using System;
using System.Collections.Generic;
using System.Linq;

namespace FluentSensors.Common.Sensors
{
    // the sensor units:
    // an LHM SensorType to its display unit, one source for every graph, row, threshold and recording
    public static class SensorUnitFormatter
    {
        // the data steps (KB, MB, GB), Clock, Frequency, Energy, Power and Current scale by this
        private const double ScaleThreshold = 1000;

        // TimeSpan steps from seconds to minutes to hours
        private const double SecondsPerMinute = 60;
        private const double SecondsPerHour = 3600;

        // the sensors report binary units (SmallData MiB, Data GiB, Throughput MiB/s via
        // HardwareMonitorService), shown as MB and GB
        // bits are decimal like advertised network speeds, so these convert to decimal megabits and gigabits
        private const double MebibytesToMegabits = 1_048_576.0 * 8 / 1_000_000;
        private const double GibibytesToGigabits = 1_073_741_824.0 * 8 / 1_000_000_000;

        // the live switches; SettingsService.DataSizeUnitBasis and DataSpeedUnitBasis persist and mirror them in here
        // size is SmallData and Data, speed is Throughput
        public static DataUnitBasis DataSizeBasis { get; set; } = DataUnitBasis.Byte;
        public static DataUnitBasis DataSpeedBasis { get; set; } = DataUnitBasis.Byte;

        // the chosen unit per category, persisted and mirrored in the same way; Frequency, Energy and TimeSpan
        // always scale automatically
        public static UnitPrefix DataSizePrefix { get; set; } = UnitPrefix.Auto;
        public static UnitPrefix DataSpeedPrefix { get; set; } = UnitPrefix.Auto;
        public static UnitPrefix ClockPrefix { get; set; } = UnitPrefix.Auto;
        public static UnitPrefix PowerPrefix { get; set; } = UnitPrefix.Auto;
        public static UnitPrefix CurrentPrefix { get; set; } = UnitPrefix.Auto;

        // --- unit ladders ---
        // smallest unit first; Factor turns the raw value into that unit

        private readonly record struct UnitStep(UnitPrefix Prefix, string Unit, double Factor);

        private static readonly UnitStep[] SmallDataBytes =
        [
            new(UnitPrefix.Kilo, "KB", ScaleThreshold),
            new(UnitPrefix.Mega, "MB", 1),
            new(UnitPrefix.Giga, "GB", 1 / ScaleThreshold)
        ];

        private static readonly UnitStep[] SmallDataBits =
        [
            new(UnitPrefix.Kilo, "kbit", MebibytesToMegabits * ScaleThreshold),
            new(UnitPrefix.Mega, "Mbit", MebibytesToMegabits),
            new(UnitPrefix.Giga, "Gbit", MebibytesToMegabits / ScaleThreshold)
        ];

        private static readonly UnitStep[] DataBytes =
        [
            new(UnitPrefix.Kilo, "KB", ScaleThreshold * ScaleThreshold),
            new(UnitPrefix.Mega, "MB", ScaleThreshold),
            new(UnitPrefix.Giga, "GB", 1)
        ];

        private static readonly UnitStep[] DataBits =
        [
            new(UnitPrefix.Kilo, "kbit", GibibytesToGigabits * ScaleThreshold * ScaleThreshold),
            new(UnitPrefix.Mega, "Mbit", GibibytesToGigabits * ScaleThreshold),
            new(UnitPrefix.Giga, "Gbit", GibibytesToGigabits)
        ];

        private static readonly UnitStep[] ThroughputBytes =
        [
            new(UnitPrefix.Kilo, "KB/s", ScaleThreshold),
            new(UnitPrefix.Mega, "MB/s", 1),
            new(UnitPrefix.Giga, "GB/s", 1 / ScaleThreshold)
        ];

        private static readonly UnitStep[] ThroughputBits =
        [
            new(UnitPrefix.Kilo, "kbit/s", MebibytesToMegabits * ScaleThreshold),
            new(UnitPrefix.Mega, "Mbit/s", MebibytesToMegabits),
            new(UnitPrefix.Giga, "Gbit/s", MebibytesToMegabits / ScaleThreshold)
        ];

        private static readonly UnitStep[] ClockSteps =
        [
            new(UnitPrefix.Mega, "MHz", 1),
            new(UnitPrefix.Giga, "GHz", 1 / ScaleThreshold)
        ];

        private static readonly UnitStep[] PowerSteps =
        [
            new(UnitPrefix.Milli, "mW", ScaleThreshold),
            new(UnitPrefix.None, "W", 1)
        ];

        private static readonly UnitStep[] CurrentSteps =
        [
            new(UnitPrefix.Milli, "mA", ScaleThreshold),
            new(UnitPrefix.None, "A", 1)
        ];

        private static readonly UnitStep[] FrequencySteps =
        [
            new(UnitPrefix.None, "Hz", 1),
            new(UnitPrefix.Kilo, "kHz", 1 / ScaleThreshold)
        ];

        private static readonly UnitStep[] EnergySteps =
        [
            new(UnitPrefix.Milli, "mWh", 1),
            new(UnitPrefix.None, "Wh", 1 / ScaleThreshold)
        ];

        private static readonly UnitStep[] TimeSpanSteps =
        [
            new(UnitPrefix.None, "s", 1),
            new(UnitPrefix.None, "min", 1 / SecondsPerMinute),
            new(UnitPrefix.None, "h", 1 / SecondsPerHour)
        ];


        // === public methods ===

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
                "Current" => "A",
                "Frequency" => "Hz",
                "Flow" => "L/h",
                "Control" or "Level" or "Humidity" => "%",
                "TimeSpan" => "s",
                "Timing" => "ns",
                "Energy" => "mWh",
                "Noise" => "dBA",
                "Signal" => "dBm", // not an LHM type; the RSSI of the Windows source
                "Conductivity" => "µS/cm",
                _ => "" // Factor, a bare number
            };
        }

        // a raw value in its display unit: the unit chosen for its category, or with Auto the largest unit that
        // keeps it at 1 or above (MHz to GHz, MB/s to KB/s); callers wanting the bare number come here too, the
        // decision lives only here
        public static (double Value, string Unit) Scale(double value, string sensorType)
        {
            var (ladder, prefix) = GetLadder(sensorType);
            if (ladder == null) return (value, GetUnit(sensorType));

            UnitStep step = prefix == UnitPrefix.Auto
                ? PickAutoStep(ladder, value, sensorType)
                : FindStep(ladder, prefix) ?? PickAutoStep(ladder, value, sensorType);

            return (value * step.Factor, step.Unit);
        }

        // a raw value in a given unit of its ladder, so a number without a unit (the hover and threshold labels)
        // matches the y-axis next to it; anything else scales on its own
        public static double ScaleToUnit(double value, string sensorType, string unit)
        {
            var (ladder, _) = GetLadder(sensorType);
            if (ladder != null)
            {
                foreach (var step in ladder)
                {
                    if (step.Unit == unit) return value * step.Factor;
                }
            }

            return Scale(value, sensorType).Value;
        }

        public static string Format(double value, string sensorType)
        {
            var (scaledValue, unit) = Scale(value, sensorType);
            return $"{scaledValue:F1} {unit}";
        }

        // the units a category can be fixed to, smallest first, in the active data unit; the settings page lists them
        // next to Auto
        public static IReadOnlyList<(UnitPrefix Prefix, string Unit)> GetSelectableUnits(string sensorType)
        {
            var (ladder, _) = GetLadder(sensorType);
            return ladder == null ? [] : ladder.Select(step => (step.Prefix, step.Unit)).ToList();
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


        // === private helpers ===

        // the ladder of a type in the active data unit, and the unit chosen for its category
        private static (UnitStep[] Ladder, UnitPrefix Prefix) GetLadder(string sensorType)
        {
            return sensorType switch
            {
                "SmallData" => (DataSizeBasis == DataUnitBasis.Bit ? SmallDataBits : SmallDataBytes, DataSizePrefix),
                "Data" => (DataSizeBasis == DataUnitBasis.Bit ? DataBits : DataBytes, DataSizePrefix),
                "Throughput" => (DataSpeedBasis == DataUnitBasis.Bit ? ThroughputBits : ThroughputBytes, DataSpeedPrefix),
                "Clock" => (ClockSteps, ClockPrefix),
                "Power" => (PowerSteps, PowerPrefix),
                "Current" => (CurrentSteps, CurrentPrefix),
                "Frequency" => (FrequencySteps, UnitPrefix.Auto),
                "Energy" => (EnergySteps, UnitPrefix.Auto),
                "TimeSpan" => (TimeSpanSteps, UnitPrefix.Auto),
                _ => (null, UnitPrefix.Auto)
            };
        }

        // the largest unit the value reads 1 or more in, else the smallest; 0 stays in the base unit, so an idle
        // sensor reads 0.0 MB/s and not 0.0 KB/s
        private static UnitStep PickAutoStep(UnitStep[] ladder, double value, string sensorType)
        {
            if (value == 0 || !double.IsFinite(value))
            {
                string baseUnit = GetUnit(sensorType);
                return ladder.FirstOrDefault(step => step.Unit == baseUnit, ladder[0]);
            }

            double magnitude = Math.Abs(value);
            for (int i = ladder.Length - 1; i > 0; i--)
            {
                if (magnitude * ladder[i].Factor >= 1) return ladder[i];
            }

            return ladder[0];
        }

        private static UnitStep? FindStep(UnitStep[] ladder, UnitPrefix prefix)
        {
            foreach (var step in ladder)
            {
                if (step.Prefix == prefix) return step;
            }

            return null;
        }
    }
}
