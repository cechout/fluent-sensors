using System.Collections.Generic;


namespace FluentSensors.Core.StaticInfo
{
    // static, one-time facts about the CPU;
    // queried once at startup, never refreshed; core count, cache size, and socket do not change while the app runs
    public record WinCpuInfo(
        int PhysicalCores,
        int LogicalProcessors,
        int MaxClockSpeedMhz, // rated/base clock as reported by SMBIOS, not real-time boost
        string SocketDesignation,

        // KNOWN UNRELIABLE:
        // Win32_Processor.VirtualizationFirmwareEnabled/VMMonitorModeExtensions have been observed reporting False
        // on systems where virtualization is confirmed enabled and working (Task Manager shows "Enabled", Hyper-V/WSL2
        // functions normally)
        // A documented, unresolved WMI provider inaccuracy on some modern systems, not something we can correct on our end
        // Confirmed case (Core Ultra 9, clean Windows install, same result):
        // https://learn.microsoft.com/en-us/answers/questions/5523363/virtualizationfirmwareenabled-false-returned-despi
        // Do not treat a False value here as ground truth without cross-checking Task Manager
        bool VirtualizationFirmwareEnabled,
        bool VirtualizationExtensionsSupported,

        IReadOnlyList<WinCpuCoreTopologyEntry> CoreTopology,
        IReadOnlyList<WinCpuCacheEntry> CacheEntries // raw per-instance cache facts, see WinCpuCacheEntry for the Level numbering
    );
}
