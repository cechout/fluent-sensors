namespace FluentSensors.Common.Sensors
{
    // the sensor type profiles:
    // defaults and steps of the threshold and y-axis controls per LHM sensor type, since percent and
    // MHz need very different scales
    // in the displayed base unit (MB or Mbit, MB/s or Mbit/s); consumers convert through
    // SensorUnitFormatter.ToRawValue on use
    public readonly struct SensorTypeProfile
    {
        public double ThresholdDefault { get; init; }
        public double ThresholdStep { get; init; }
        public double YMaxDefault { get; init; }
        public double YMaxStep { get; init; }
    }

    public static class SensorTypeProfiles
    {
        // the fallback
        private static readonly SensorTypeProfile Default = new()
        {
            ThresholdDefault = 50,
            ThresholdStep = 5,
            YMaxDefault = 100,
            YMaxStep = 10
        };

        // thousands of MHz, the fallback step would need hundreds of clicks
        private static readonly SensorTypeProfile Clock = new()
        {
            ThresholdDefault = 2000,
            ThresholdStep = 100,
            YMaxDefault = 3000,
            YMaxStep = 100
        };

        // cumulative over uptime, into the hundreds of GB
        private static readonly SensorTypeProfile Data = new()
        {
            ThresholdDefault = 20,
            ThresholdStep = 1,
            YMaxDefault = 50,
            YMaxStep = 10
        };

        // gpu memory and similar, low thousands of MB
        private static readonly SensorTypeProfile SmallData = new()
        {
            ThresholdDefault = 500,
            ThresholdStep = 50,
            YMaxDefault = 1000,
            YMaxStep = 100
        };

        // a few hundred to a few thousand rpm
        private static readonly SensorTypeProfile Fan = new()
        {
            ThresholdDefault = 2000,
            ThresholdStep = 100,
            YMaxDefault = 3000,
            YMaxStep = 100
        };

        // a compromise, the type spans core voltage (0.8 to 1.5 V) and psu rails (3.3, 5, 12 V); precise
        // for the core, coarse for rails
        private static readonly SensorTypeProfile Voltage = new()
        {
            ThresholdDefault = 1.5,
            ThresholdStep = 0.1,
            YMaxDefault = 2.0,
            YMaxStep = 0.1
        };

        // disk and network, MB/s from HardwareMonitorService; tuned for SSD write bursts, idle sits near 0
        private static readonly SensorTypeProfile Throughput = new()
        {
            ThresholdDefault = 50,
            ThresholdStep = 10,
            YMaxDefault = 100,
            YMaxStep = 25
        };

        // battery capacity in mWh, tens of thousands
        private static readonly SensorTypeProfile Energy = new()
        {
            ThresholdDefault = 20000,
            ThresholdStep = 1000,
            YMaxDefault = 100000,
            YMaxStep = 5000
        };

        // seconds; battery runtime in hours, psu uptime far beyond
        private static readonly SensorTypeProfile TimeSpan = new()
        {
            ThresholdDefault = 1800,
            ThresholdStep = 300,
            YMaxDefault = 36000,
            YMaxStep = 1800
        };

        // battery and rail currents, a few A
        private static readonly SensorTypeProfile Current = new()
        {
            ThresholdDefault = 5,
            ThresholdStep = 0.5,
            YMaxDefault = 10,
            YMaxStep = 1
        };

        // the raw SensorType.ToString() ("Clock", "Load")
        public static SensorTypeProfile GetProfile(string sensorType)
        {
            return sensorType switch
            {
                "Energy" => Energy,
                "TimeSpan" => TimeSpan,
                "Current" => Current,
                "Clock" => Clock,
                "Data" => Data,
                "SmallData" => SmallData,
                "Fan" => Fan,
                "Voltage" => Voltage,
                "Throughput" => Throughput,
                _ => Default
            };
        }
    }
}
