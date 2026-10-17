using System.Collections.Generic;


namespace FluentSensors.Core.StaticInfo
{
    // what a monitor answers over DDC/CI beyond its levels; every field null when it does not answer that code
    // KNOWN UNRELIABLE: see WinDisplayReader, the DDC/CI part
    public record WinDisplayDdcInfo(
        uint? PowerMode, // VCP D6, see HardwareInfoFormatter.FormatDdcPowerMode
        uint? InputSource, // VCP 60, see HardwareInfoFormatter.FormatDdcInputSource
        uint? UsageHours, // VCP C0
        string FirmwareLevel, // VCP C9 as "2.1", "" without one
        uint? Technology, // VCP B6, see HardwareInfoFormatter.FormatDdcTechnology
        string MccsVersion // VCP DF as "2.1", "" without one
    );

    // one active display, from the display config, its EDID, DXGI and DDC/CI
    public record WinDisplayInfo(
        string Name, // the EDID name ("DELL U2720Q"), else a name of ours; unique, the hardware name of its sensors
        string Key, // stable id part of its sensors, from the EDID ids ("auoc199")
        string InstancePath, // "DISPLAY\AUOC199\4&3861AF4C&0&UID8388688", what a root\wmi InstanceName starts with; "" unknown
        string GdiDeviceName, // "\\.\DISPLAY1", the desktop source it shows; shared by mirrored displays
        long AdapterLuid, // the gpu that scans it out
        bool IsBuiltIn, // the laptop panel
        bool IsPrimary, // its desktop starts at 0,0
        uint OutputTechnology, // raw, see HardwareInfoFormatter.FormatDisplayConnector
        uint Rotation, // raw DISPLAYCONFIG_ROTATION, see HardwareInfoFormatter.FormatDisplayOrientation
        int PositionX, // top left of its desktop
        int PositionY,
        int WidthPixels, // the desktop mode; 0 when it is not readable
        int HeightPixels,
        double RefreshRateHz, // moves with Dynamic Refresh Rate
        bool IsHdrSupported,
        bool IsHdrEnabled,
        int BitsPerColorChannel, // 0 when not reported

        // from the EDID ids of the display config
        string ManufacturerId, // the three letter PNP id ("AUO"), "" without one
        string ProductCode, // four hex digits ("C199"), "" without one

        // the details, only filled when asked for (see WinDisplayReader.QueryDisplays)
        int NativeWidthPixels = 0, // the preferred mode of the panel
        int NativeHeightPixels = 0,
        int ScalePercent = 0, // 150 for 150 %
        int? SdrWhiteLevelNits = null, // brightness of SDR content while HDR is on
        double? MaxLuminanceNits = null, // as the display reports it to DXGI
        double? MinLuminanceNits = null,
        int? ManufactureYear = null,
        int? ManufactureWeek = null, // 1 to 54; null when the EDID has none
        int? WidthCentimeters = null, // the visible area; null for a projector or without EDID
        int? HeightCentimeters = null,
        double? Gamma = null,
        WinDisplayDdcInfo? Ddc = null // null when the monitor does not answer at all (most laptop panels)
    );
}
