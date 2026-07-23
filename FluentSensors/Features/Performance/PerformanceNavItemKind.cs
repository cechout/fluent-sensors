namespace FluentSensors.Features.Performance
{
    // identifies which hardware category a PerformanceNavItemViewModel represents; drives both which detail
    // block is shown and which group label the header uses
    public enum PerformanceNavItemKind
    {
        Cpu,
        Ram,
        Gpu,
        Storage,
        Network
    }
}