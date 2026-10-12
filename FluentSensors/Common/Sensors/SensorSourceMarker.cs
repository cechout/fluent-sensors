using System;


namespace FluentSensors.Common.Sensors
{
    // the source marker:
    // plain text beside a sensor title that says who measured the value, LibreHardwareMonitor or Windows
    // display only; the sensor name itself stays untouched, it is the csv header and the key of the saved selections
    public static class SensorSourceMarker
    {
        // every sensor id of the Windows source starts with it, an LHM identifier never does
        public const string WindowsIdRoot = "/win/";

        private const string WindowsMarker = "[Win]";
        private const string LhmMarker = "[LHM]";

        public static bool IsWindowsSensor(string sensorId) =>
            sensorId != null && sensorId.StartsWith(WindowsIdRoot, StringComparison.Ordinal);

        public static string Get(string sensorId) => IsWindowsSensor(sensorId) ? WindowsMarker : LhmMarker;

        public static string Append(string title, string sensorId) => $"{title} {Get(sensorId)}";
    }
}
