namespace FluentSensors.Core.StaticInfo
{
    // one active display, from the display config and its EDID
    public record WinDisplayInfo(
        string Name, // the EDID name ("DELL U2720Q"), else a name of ours; unique, the hardware name of its sensors
        string Key, // stable id part of its sensors, from the EDID ids ("auoc199")
        string InstancePath, // "DISPLAY\AUOC199\4&3861AF4C&0&UID8388688", what a root\wmi InstanceName starts with; "" unknown
        long AdapterLuid, // the gpu that scans it out
        bool IsBuiltIn, // the laptop panel
        uint OutputTechnology, // raw, see HardwareInfoFormatter.FormatDisplayConnector
        int WidthPixels, // 0 when the mode is not readable
        int HeightPixels,
        double RefreshRateHz, // moves with Dynamic Refresh Rate
        bool IsHdrSupported,
        bool IsHdrEnabled,
        int BitsPerColorChannel, // 0 when not reported

        // from the EDID
        string ManufacturerId, // the three letter PNP id ("AUO"), "" without one
        int? ManufactureYear,
        int? WidthCentimeters, // the visible area; null for a projector or without EDID
        int? HeightCentimeters
    );
}
