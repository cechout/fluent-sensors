namespace FluentSensors.Common.Sensors
{
    public static class NetworkDirectionLabel
    {
        public static string Resolve(string name, string sensorType) => sensorType == "Throughput" ? name switch
        {
            "Download" or "Download Speed" => "\u2B07",
            "Upload" or "Upload Speed" => "\u2B06",
            _ => name
        } : name;
    }
}
