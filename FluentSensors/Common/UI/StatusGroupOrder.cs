namespace FluentSensors.Common.UI
{
    // which of the two title bar status groups is placed first
    // the second one is also the one that gives way when the window gets too narrow for both
    public enum StatusGroupOrder
    {
        LhmFirst, // sensor readout first, this apps own resource usage after it
        WindowsFirst
    }
}
