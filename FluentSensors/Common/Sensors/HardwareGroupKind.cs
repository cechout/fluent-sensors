namespace FluentSensors.Common.Sensors
{
    // the hardware category of a sensor group on every page; labels and icons through HardwareGroupInfo
    public enum HardwareGroupKind
    {
        Cpu,
        Ram,
        DiscreteGpu,
        IntegratedGpu,
        Storage,
        Network,
        Battery,

        // not LHM hardware; its sensors come from the Windows source
        Display,

        // the rest (motherboard, fan and AIO controllers like Aquacomputer or Corsair Commander); keeps GetKind total
        Other
    }
}
