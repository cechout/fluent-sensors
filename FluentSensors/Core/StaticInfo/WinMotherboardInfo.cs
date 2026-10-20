namespace FluentSensors.Core.StaticInfo
{
    // the board;
    // Manufacturer, Product and Version can be blank or "To Be Filled By O.E.M.", a widespread firmware
    // default, undocumented but called out by a kernel maintainer:
    // https://lkml.iu.edu/hypermail/linux/kernel/0912.0/03206.html
    public record WinMotherboardInfo(
        string Manufacturer,
        string Product,
        string Version,
        string BiosVersion,
        string BiosReleaseDate,

        // the device the board sits in, as its maker names it ("ROG Zephyrus M16"); often a firmware default on a
        // self-built desktop
        string SystemFamily,
        string SystemModel,

        // raw SMBIOS Type 3 "Chassis Type", 0 when not reported; see HardwareInfoFormatter.FormatChassisType
        uint ChassisType,

        string EmbeddedControllerVersion, // "3.10", "" on a board without one
        bool? SecureBootEnabled, // null on a legacy BIOS, which has no secure boot
        string TpmVersion // "2.0", "" without a TPM
    );
}
