namespace FluentSensors.Core.StaticInfo
{
    // static facts about one physical RAM module (DIMM)
    public record WinMemoryModuleInfo(
        string Manufacturer,
        string PartNumber,
        ulong CapacityBytes,
        uint ConfiguredClockSpeedMhz,
        uint SmbiosMemoryType, // raw SMBIOS Type 17 value, e.g. 26 = DDR4, 34 = DDR5
        string DeviceLocator, // e.g. "DIMM0"; which slot this module sits in
        int TotalWidthBits,
        int DataWidthBits // TotalWidth > DataWidth is the standard signal for ECC support
    );
}