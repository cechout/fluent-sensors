namespace FluentSensors.Common
{
    // maps a LibreHardwareMonitor SensorType string to its display unit; single source of truth for anything
    // on the Performance page, so every graph formats its value label the same way
    public static class SensorUnitFormatter
    {
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

        public static string Format(double value, string sensorType)
        {
            return $"{value:F1} {GetUnit(sensorType)}";
        }
    }
}