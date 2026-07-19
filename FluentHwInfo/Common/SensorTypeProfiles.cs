namespace FluentHwInfo.Common
{
    // starting values for threshold and y-axis controls, tuned per LibreHardwareMonitor sensor type a "%"-based sensor
    // and a "MHz"-based sensor need vastly different scales, this table is the single place that maps a sensor type to
    // its own default value and step size
    public readonly struct SensorTypeProfile
    {
        public double ThresholdDefault { get; init; }
        public double ThresholdStep { get; init; }
        public double YMaxDefault { get; init; }
        public double YMaxStep { get; init; }
    }

    public static class SensorTypeProfiles
    {
        // fallback profile, matches the values every sensor used before per-type profiles existed
        private static readonly SensorTypeProfile Default = new()
        {
            ThresholdDefault = 50,
            ThresholdStep = 5,
            YMaxDefault = 100,
            YMaxStep = 10
        };

        // clock speeds sit in the thousands (MHz), the generic default/step would need hundreds of clicks to reach a
        // realistic value
        private static readonly SensorTypeProfile Clock = new()
        {
            ThresholdDefault = 3000,
            ThresholdStep = 100,
            YMaxDefault = 5000,
            YMaxStep = 250
        };

        // cumulative read/written data over uptime, can climb into the hundreds of GB
        private static readonly SensorTypeProfile Data = new()
        {
            ThresholdDefault = 500,
            ThresholdStep = 50,
            YMaxDefault = 1000,
            YMaxStep = 100
        };

        // gpu memory usage and similar, commonly in the low thousands of MB
        private static readonly SensorTypeProfile SmallData = new()
        {
            ThresholdDefault = 500,
            ThresholdStep = 50,
            YMaxDefault = 1000,
            YMaxStep = 100
        };

        // case/gpu fans, typically a few hundred to a few thousand rpm
        private static readonly SensorTypeProfile Fan = new()
        {
            ThresholdDefault = 2000,
            ThresholdStep = 100,
            YMaxDefault = 3000,
            YMaxStep = 250
        };

        // compromise profile: this sensor type spans both cpu core voltage (~0.8-1.5V) and psu rails (3.3V/5V/12V)
        // under the same type, so this is tuned for core voltage precision and stays coarse for rail voltages
        private static readonly SensorTypeProfile Voltage = new()
        {
            ThresholdDefault = 1.5,
            ThresholdStep = 0.1,
            YMaxDefault = 2.0,
            YMaxStep = 0.2
        };

        // sensorType is the raw LibreHardwareMonitor SensorType.ToString() value (e.g. "Clock", "Load")
        public static SensorTypeProfile GetProfile(string sensorType)
        {
            return sensorType switch
            {
                "Clock" => Clock,
                "Data" => Data,
                "SmallData" => SmallData,
                "Fan" => Fan,
                "Voltage" => Voltage,
                _ => Default
            };
        }
    }
}