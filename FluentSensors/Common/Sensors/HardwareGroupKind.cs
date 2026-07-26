namespace FluentSensors.Common.Sensors
{
    // identifies which broad hardware category a group of sensors belongs to, regardless of which page displays
    // them (SensorsPage or the Performance page)
    // drives group labels and icons via HardwareGroupInfo
    public enum HardwareGroupKind
    {
        Cpu,
        Ram,
        Gpu,
        Storage,
        Network,

        // anything LHM reports outside the above (e.g. Motherboard, fan/AIO controller chips like Aquacomputer
        // or Corsair Commander); rare in practice, but keeps GetKind total instead of needing a nullable return
        Other
    }
}