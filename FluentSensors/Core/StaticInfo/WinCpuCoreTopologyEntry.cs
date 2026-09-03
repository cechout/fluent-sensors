using System.Collections.Generic;


namespace FluentSensors.Core.StaticInfo
{
    // raw per-physical-core topology info from GetLogicalProcessorInformationEx;
    // intentionally does NOT label anything as "P-Core"/"E-Core" here; EfficiencyClass is the raw hardware value
    // Windows itself exposes, interpreting/labeling it is a UI-layer decision (see
    // LhmCpuInstanceViewModel.FormatCoreTopology), not a backend fact
    public record WinCpuCoreTopologyEntry(
        int CoreIndex, // physical core index, order as returned by Windows (0-based)
        byte EfficiencyClass, // higher = more "performance"-oriented; exact scale is CPU-vendor-specific
        bool HasSmt, // true if this physical core exposes more than one logical processor
        IReadOnlyList<int> LogicalProcessorIndices // bit positions within this cores affinity mask
    );
}
