namespace FluentSensors.Core.StaticInfo
{
    // one raw Win32_CacheMemory entry of the processor (GetRelated)
    // Level stays raw: 3 = L1, 4 = L2, 5 = L3, measured on real hardware, not documented (unlike CacheType, see
    // HardwareInfoFormatter.FormatCacheType)
    // one cache can appear repeatedly (L3 once per core group); HardwareInfoFormatter.FormatCacheLevelTotal dedupes
    public record WinCpuCacheEntry(
        uint Level, // raw numbering, see above
        string CacheTypeText,
        uint SizeKb
    );
}
