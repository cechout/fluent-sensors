namespace FluentSensors.Common.UI
{
    // which title bar status group comes first; the second gives way in a narrow window
    public enum StatusGroupOrder
    {
        LhmFirst, // the sensor readout, then the app usage
        WindowsFirst
    }
}
