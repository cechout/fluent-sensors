namespace FluentSensors.Common.Sensors
{
    public static class NetworkDirectionLabel
    {
        public static string Resolve(string name, string sensorType) => sensorType == "Throughput" ? name switch
        {
            "Download" or "Download Speed" => "\u2193",
            "Upload" or "Upload Speed" => "\u2191",
            _ => name
        } : name;
    }
}
