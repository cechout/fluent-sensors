using System.Collections.Generic;


namespace FluentSensors.Core.StaticInfo
{
    // one GPU; not Win32_VideoController.AdapterRAM, a uint32 that truncates 4 GB and more (undocumented but
    // reproduced, an 8 GB RTX 2070 reports 4293918720):
    // https://forums.developer.nvidia.com/t/how-to-query-adapter-ram-for-cards-with-more-than-4-gb-c/69955
    // https://github.com/glpi-project/glpi-agent/issues/199
    public record WinGpuInfo(
        string Name,
        string DriverVersion,
        string PnpDeviceId,

        // from DXGI (IDXGIAdapter1.Description1), not WMI
        uint VendorId,
        uint DeviceId,
        ulong DedicatedVideoMemoryBytes,
        ulong DedicatedSystemMemoryBytes,
        ulong SharedSystemMemoryBytes,
        long AdapterLuid, // 0 without a DXGI match

        // where the picture of this gpu goes, see WinDisplayTopology; read once at startup like the rest
        IReadOnlyList<WinDisplay> Displays, // the displays it scans out itself
        string? DisplayViaGpuName // the integrated gpu a hybrid discrete one renders through, when it drives none
    );
}
