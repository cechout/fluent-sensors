namespace FluentSensors.Core.StaticInfo
{
    // static facts about one physical RAM module (DIMM)
    public record WinMemoryModuleInfo(
        string Manufacturer,
        string PartNumber,
        ulong CapacityBytes,
        uint ConfiguredClockSpeedMhz,

        // raw SMBIOS Type 17 "Memory Type" value
        // 26 = DDR4 is confirmed by Microsofts own docs:
        // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-physicalmemory)
        // 34 = DDR5 is NOT in that doc (it predates DDR5); sourced instead directly from the DMTF SMBIOS spec itself:
        // https://www.dmtf.org/sites/default/files/standards/documents/DSP0134_3.9.0.pdf
        // table "Memory Device - Type"
        uint SmbiosMemoryType,

        string DeviceLocator, // e.g. "DIMM0"; which slot this module sits in
        int TotalWidthBits,

        // TotalWidth > DataWidth (typically 72 vs 64 bits) is how the SMBIOS spec itself represents ECC: the extra
        // bits store the error-correction "syndrome"
        // Confirmed by Linux kernels own docs (Documentation/admin-guide/ras.rst) and widely visible in real dmidecode
        // output on ECC systems
        int DataWidthBits
    );
}