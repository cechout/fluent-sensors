namespace FluentSensors.Core.StaticInfo
{
    // one physical RAM module (DIMM)
    public record WinMemoryModuleInfo(
        string Manufacturer,
        string PartNumber,
        string SerialNumber,
        ulong CapacityBytes,
        uint ConfiguredClockSpeedMhz,

        // the rated speed (3600 for a "3600 MT/s" kit); above ConfiguredClockSpeedMhz when the module
        // runs below it (XMP or EXPO off)
        uint RatedSpeedMhz,

        // raw SMBIOS Type 17 "Memory Type"; 26 = DDR4 per the Microsoft docs:
        // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-physicalmemory
        // 34 = DDR5 is newer than that doc, from the DMTF SMBIOS spec, table "Memory Device - Type":
        // https://www.dmtf.org/sites/default/files/standards/documents/DSP0134_3.9.0.pdf
        uint SmbiosMemoryType,

        string DeviceLocator, // the slot; "DIMM0"
        string BankLabel, // the bank of several slots; "BANK 0"

        // raw SMBIOS Type 17 "Form Factor" (9 = DIMM, 13 = SODIMM, the table in
        // HardwareInfoFormatter.FormatFormFactor); from the DMTF spec and dmidecode output, the
        // Microsoft docs are unreliable here
        uint FormFactor,

        // raw SMBIOS Type 17 "Attributes", the rank (1 = single, 2 = dual); 0 when the firmware did not report it
        uint Rank,

        uint ConfiguredVoltageMillivolts,
        uint MinVoltageMillivolts,
        uint MaxVoltageMillivolts,

        int TotalWidthBits,

        // TotalWidth over DataWidth (72 to 64 bits) is how SMBIOS shows ECC, the extra bits hold the syndrome; per the
        // Linux docs (Documentation/admin-guide/ras.rst) and dmidecode output
        int DataWidthBits
    );
}
