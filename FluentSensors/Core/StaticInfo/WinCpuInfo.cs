using System.Collections.Generic;


namespace FluentSensors.Core.StaticInfo
{
    // the CPU facts, queried once at startup
    public record WinCpuInfo(
        int PhysicalCores,
        int LogicalProcessors,
        int MaxClockSpeedMhz, // the SMBIOS base clock, not the live boost
        string SocketDesignation,

        // KNOWN UNRELIABLE:
        // Win32_Processor.VirtualizationFirmwareEnabled and VMMonitorModeExtensions report False with
        // virtualization on and working (Task Manager "Enabled", Hyper-V and WSL2 run); an open WMI fault, Core
        // Ultra 9 on a clean install:
        // https://learn.microsoft.com/en-us/answers/questions/5523363/virtualizationfirmwareenabled-false-returned-despi
        // a False is no ground truth without Task Manager
        bool VirtualizationFirmwareEnabled,
        bool VirtualizationExtensionsSupported,

        IReadOnlyList<WinCpuCoreTopologyEntry> CoreTopology,
        IReadOnlyList<WinCpuCacheEntry> CacheEntries // raw per instance, see WinCpuCacheEntry for the levels
    );
}
