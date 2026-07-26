using System.Collections.Generic;


namespace FluentSensors.Core.StaticInfo
{
    public record WinMemoryInfo(
        int TotalSlots,
        IReadOnlyList<WinMemoryModuleInfo> Modules
    );
}