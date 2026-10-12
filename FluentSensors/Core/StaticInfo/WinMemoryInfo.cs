using System.Collections.Generic;


namespace FluentSensors.Core.StaticInfo
{
    public record WinMemoryInfo(
        int TotalSlots,
        ulong MaxCapacityBytes, // what the board takes at most, 0 when not reported

        // raw SMBIOS Type 16 "Memory Error Correction", see HardwareInfoFormatter.FormatErrorCorrection
        uint ErrorCorrection,
        IReadOnlyList<WinMemoryModuleInfo> Modules
    );
}
