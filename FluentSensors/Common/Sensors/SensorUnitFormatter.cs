namespace FluentSensors.Common.Sensors
{
    // maps a LibreHardwareMonitor SensorType string to its display unit; single source of truth for anything
    // on the Performance page, so every graph formats its value label the same way
    public static class SensorUnitFormatter
    {
        // Clock, SmallData and Throughput switch to a bigger unit once a raw value reaches this
        private const double ScaleThreshold = 1000;

        public static string GetUnit(string sensorType)
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
                "SmallData" when value >= ScaleThreshold => (value / ScaleThreshold, "GB"),
                "Throughput" when value >= ScaleThreshold => (value / ScaleThreshold, "GB/s"),
                _ => (value, GetUnit(sensorType))
            };
        }

        public static string Format(double value, string sensorType)
        {
            var (scaledValue, unit) = Scale(value, sensorType);
            return $"{scaledValue:F1} {unit}";
        }
    }
}