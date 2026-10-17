using System;


namespace FluentSensors.Common.Sensors
{
    // the source of a sensor:
    // LibreHardwareMonitor or Windows, told apart by the id; the sensor name itself never says it, it is the csv
    // header and the key of the saved selections (the icon behind a title is SensorSourceIcon)
    public static class SensorSourceMarker
    {
        // every sensor id of the Windows source starts with it, an LHM identifier never does
        public const string WindowsIdRoot = "/win/";

        public static bool IsWindowsSensor(string sensorId) =>
            sensorId != null && sensorId.StartsWith(WindowsIdRoot, StringComparison.Ordinal);
    }
}
