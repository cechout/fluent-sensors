namespace FluentSensors.Core.StaticInfo
{
    // Manufacturer/Product/Version can legitimately be blank or "To Be Filled By O.E.M."; a known, widespread
    // SMBIOS/DMI firmware quirk (dodgy vendor BIOS defaults, not Windows- or us-specific), not a bug on our end
    // No single official spec page documents the exact placeholder string, but its confirmed cross-platform,
    // e.g. a Linux kernel maintainer calling it out directly:
    // https://lkml.iu.edu/hypermail/linux/kernel/0912.0/03206.html
    public record WinMotherboardInfo(
        string Manufacturer,
        string Product,
        string Version,
        string BiosVersion,
        string BiosReleaseDate
    );
}