namespace FluentSensors.Core.StaticInfo
{
    // the board; Manufacturer, Product and Version can be blank or "To Be Filled By O.E.M.", a widespread firmware
    // default, undocumented but called out by a kernel maintainer:
    // https://lkml.iu.edu/hypermail/linux/kernel/0912.0/03206.html
    public record WinMotherboardInfo(
        string Manufacturer,
        string Product,
        string Version,
        string BiosVersion,
        string BiosReleaseDate
    );
}
