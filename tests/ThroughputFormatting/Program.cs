using System.Globalization;
using FluentSensors.Common.Sensors;

var checks = 0;
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, got {actual}");
    checks++;
}

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
SensorUnitFormatter.DataSpeedBasis = DataUnitBasis.Byte;
foreach (var (raw, expected) in new (double, string)[]
{
    (0, "0.0 KB/s"),
    (1.0 / 1024, "1.0 KB/s"),
    (25.0 / 1024, "25.0 KB/s"),
    (1023.0 / 1024, "1023.0 KB/s"),
    (1, "1.0 MB/s"),
    (2.4, "2.4 MB/s"),
    (999, "999.0 MB/s"),
    (1000, "1.0 GB/s"),
    (0.25, "256.0 KB/s"), // the same source can rise and fall across the boundary
    (2, "2.0 MB/s")
})
    Equal(expected, SensorUnitFormatter.Format(raw, "Throughput"));

// The graph axis and formatted readout must agree on the scaled number and unit.
Equal((256.0, "KB/s"), SensorUnitFormatter.Scale(0.25, "Throughput"));
Equal("MB/s", SensorUnitFormatter.GetRawUnit("Throughput")); // CSV contract
Equal("MB/s", SensorUnitFormatter.GetUnit("Throughput")); // threshold/editor base unit
Equal(0.25, SensorUnitFormatter.ToRawValue(0.25, "Throughput"));
Equal("25.0 %", SensorUnitFormatter.Format(25, "Load"));
Equal("512.0 MB", SensorUnitFormatter.Format(512, "SmallData"));
Equal("2.0 GHz", SensorUnitFormatter.Format(2000, "Clock"));

SensorUnitFormatter.DataSpeedBasis = DataUnitBasis.Bit;
Equal("2.1 Mbit/s", SensorUnitFormatter.Format(0.25, "Throughput"));
Equal("8.4 Gbit/s", SensorUnitFormatter.Format(1000, "Throughput"));
Equal("MB/s", SensorUnitFormatter.GetRawUnit("Throughput"));
SensorUnitFormatter.DataSpeedBasis = DataUnitBasis.Byte;
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
Equal("25,5 KB/s", SensorUnitFormatter.Format(25.5 / 1024, "Throughput"));
Console.WriteLine($"PASS: {checks} throughput, boundary, raw-unit and culture checks.");
