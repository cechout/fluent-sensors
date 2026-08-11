namespace FluentSensors.Core.StaticInfo
{
    // one raw entry from Win32_CacheMemory, associated with the processor via GetRelated()
    // Level is kept as the raw WMI value, not translated to "L1"/"L2"/"L3" here: Win32_CacheMemory numbers cache
    // levels differently from the familiar cache names, confirmed on real hardware as 3=L1, 4=L2, 5=L3; unlike
    // CacheType (see HardwareInfoFormatter.FormatCacheType) this is not backed by an official Microsoft spec
    // table or smth
    // the same physical cache can appear more than once (e.g. L3, once per core group its associated with);
    // deduping is the consumers job, see HardwareInfoFormatter.FormatCacheLevelTotal
    public record WinCpuCacheEntry(
        uint Level, // raw Win32_CacheMemory numbering, see class comment above
        string CacheTypeText,
        uint SizeKb
    );
}