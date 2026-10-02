using System.Collections.Generic;


namespace FluentSensors.Core.StaticInfo
{
    // one physical core from GetLogicalProcessorInformationEx; no P or E label, the UI interprets EfficiencyClass (see
    // LhmCpuInstanceViewModel.FormatCoreTopology)
    public record WinCpuCoreTopologyEntry(
        int CoreIndex, // 0-based, in Windows order
        byte EfficiencyClass, // higher means more performance; the scale is per vendor
        bool HasSmt, // more than one logical processor
        IReadOnlyList<int> LogicalProcessorIndices // bit positions in the affinity mask
    );
}
