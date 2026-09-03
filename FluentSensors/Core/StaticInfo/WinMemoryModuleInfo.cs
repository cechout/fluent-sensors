namespace FluentSensors.Core.StaticInfo
{
    // static facts about one physical RAM module (DIMM)
    public record WinMemoryModuleInfo(
        string Manufacturer,
        string PartNumber,
        string SerialNumber,
        ulong CapacityBytes,
        uint ConfiguredClockSpeedMhz,

        // the modules rated/advertised maximum speed (e.g. 3600 for a "3600 MT/s" kit); can be higher than
        // ConfiguredClockSpeedMhz if the module is running below its rated spec (e.g. XMP/EXPO profile not
        // enabled)
        uint RatedSpeedMhz,

        // raw SMBIOS Type 17 "Memory Type" value
        // 26 = DDR4 is confirmed by Microsofts own docs:
        // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-physicalmemory)
        // 34 = DDR5 is NOT in that doc (it predates DDR5); sourced instead directly from the DMTF SMBIOS spec itself:
        // https://www.dmtf.org/sites/default/files/standards/documents/DSP0134_3.9.0.pdf
        // table "Memory Device - Type"
        uint SmbiosMemoryType,

        string DeviceLocator, // e.g. "DIMM0"; which slot this module sits in
        string BankLabel, // e.g. "BANK 0"; groups slots into memory banks, distinct from DeviceLocator

        // raw SMBIOS Type 17 "Form Factor" value (e.g. 9 = DIMM, 13 = SODIMM); see
        // HardwareInfoFormatter.FormatFormFactor for the full table
        // sourced from the DMTF SMBIOS spec directly and cross-checked against real dmidecode output, since Microsofts
        // own Win32_PhysicalMemory docs dont spell this table out reliably
        uint FormFactor,

        // raw SMBIOS Type 17 "Attributes" value; represents the memory Rank (1 = single rank, 2 = dual rank, ...);
        // 0 means the firmware did not report it
        uint Rank,

        uint ConfiguredVoltageMillivolts,
        uint MinVoltageMillivolts,
        uint MaxVoltageMillivolts,

        int TotalWidthBits,

        // TotalWidth > DataWidth (typically 72 vs 64 bits) is how the SMBIOS spec itself represents ECC: the extra
        // bits store the error-correction "syndrome"
        // Confirmed by Linux kernels own docs (Documentation/admin-guide/ras.rst) and widely visible in real dmidecode
        // output on ECC systems
        int DataWidthBits
    );
}
