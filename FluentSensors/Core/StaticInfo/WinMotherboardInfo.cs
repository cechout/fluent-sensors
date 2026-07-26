namespace FluentSensors.Core.StaticInfo
{
    // Manufacturer/Product/Version can legitimately be blank or "To Be Filled By O.E.M."; a known, widespread
    // SMBIOS firmware quirk, not a bug on our end
    public record WinMotherboardInfo(
        string Manufacturer,
        string Product,
        string Version,
        string BiosVersion,
        string BiosReleaseDate
    );
}