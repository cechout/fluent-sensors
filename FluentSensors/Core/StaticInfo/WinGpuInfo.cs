namespace FluentSensors.Core.StaticInfo
{
    // static facts about a GPU;
    // Win32_VideoController.AdapterRAM is also deliberately not used: its a uint32 field, so any card with 4GB+
    // VRAM wraps/truncates; not officially documented as broken by Microsoft, but widely reproduced, e.g. an
    // 8GB RTX 2070 reporting exactly 4293918720 bytes (~4GB):
    // https://forums.developer.nvidia.com/t/how-to-query-adapter-ram-for-cards-with-more-than-4-gb-c/69955
    // https://github.com/glpi-project/glpi-agent/issues/199
    public record WinGpuInfo(
        string Name,
        string DriverVersion,
        string PnpDeviceId,

        // everything below comes from DXGI (IDXGIAdapter1.Description1), not WMI
        uint VendorId,
        uint DeviceId,
        ulong DedicatedVideoMemoryBytes,
        ulong DedicatedSystemMemoryBytes,
        ulong SharedSystemMemoryBytes
    );
}