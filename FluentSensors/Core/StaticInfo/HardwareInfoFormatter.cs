using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace FluentSensors.Core.StaticInfo
{
    // formatting of the WinStaticInfoService facts for the hardware view info panels
    public static class HardwareInfoFormatter
    {
        // === shared ===

        public static string FormatYesNo(bool value) => value ? "Yes" : "No";

        public static string FormatMhz(uint speedMhz) => $"{speedMhz} MHz";

        public static string FormatBytesAsGb(ulong bytes)
        {
            double gb = bytes / 1024.0 / 1024.0 / 1024.0;
            return $"{gb:0.#} GB";
        }


        // === cpu ===

        // sums the distinct Win32_CacheMemory entries of a Level; distinct by type and size, so P and E core L1 both
        // count while an L3 reported once per core group counts once
        public static string FormatCacheLevelTotal(IReadOnlyList<WinCpuCacheEntry> entries, uint level)
        {
            if (entries == null) return "-";

            var distinctSizes = entries
                .Where(e => e.Level == level)
                .Select(e => new { e.CacheTypeText, e.SizeKb })
                .Distinct()
                .Select(e => e.SizeKb)
                .ToList();

            if (distinctSizes.Count == 0) return "-";

            uint totalKb = (uint)distinctSizes.Sum(s => (long)s);
            return FormatCacheSizeKb(totalKb);
        }

        private static string FormatCacheSizeKb(uint sizeKb) =>
            sizeKb >= 1024 ? $"{sizeKb / 1024.0:0.##} MB" : $"{sizeKb} KB";

        // Win32_CacheMemory.CacheType; unlike Level (see WinCpuCacheEntry) documented by Microsoft
        public static string FormatCacheType(uint cacheType)
        {
            return cacheType switch
            {
                1 => "Other",
                2 => "Unknown",
                3 => "Instruction",
                4 => "Data",
                5 => "Unified",
                _ => $"Unknown ({cacheType})"
            };
        }


        // === gpu ===

        public static string FormatPciId(uint id) => $"0x{id:X4}";

        // PCI-SIG vendor IDs
        public static string FormatVendorName(uint vendorId)
        {
            return vendorId switch
            {
                0x10DE => "NVIDIA",
                0x1002 => "AMD",
                0x8086 => "Intel",
                _ => $"Unknown (0x{vendorId:X4})"
            };
        }


        // === ram ===

        // the SMBIOS Memory Device Type (Win32_PhysicalMemory.SMBIOSMemoryType); consumer values only, others stay
        // numeric (DMTF SMBIOS spec, Memory Device, Type)
        public static string FormatMemoryType(uint smbiosType)
        {
            return smbiosType switch
            {
                20 => "DDR",
                21 => "DDR2",
                22 => "DDR2 FB-DIMM",
                24 => "DDR3",
                26 => "DDR4",
                34 => "DDR5",
                _ => $"Unknown ({smbiosType})"
            };
        }

        // the "ClockSpeed" fields carry the DDR transfer rate in MT/s, twice the real clock
        public static string FormatMemorySpeed(uint speedMts) => $"{speedMts} MT/s";

        // --- workaround: Win32_PhysicalMemory.FormFactor off-by-one vs SMBIOS spec ---
        // problem: the DMTF table starts at 1=Other, the WMI provider reports one lower; the spec numbering read a
        // SO-DIMM (CPU-Z SPD) as "RIMM" (12), a desktop DIMM came as 8; no public issue found
        // fix: the DMTF list shifted down by one, verified on those two systems
        public static string FormatFormFactor(uint formFactor)
        {
            return formFactor switch
            {
                0 => "Other",
                1 => "Unknown",
                2 => "SIMM",
                3 => "SIP",
                4 => "Chip",
                5 => "DIP",
                6 => "ZIP",
                7 => "Proprietary Card",
                8 => "DIMM",
                9 => "TSOP",
                10 => "Row of chips",
                11 => "RIMM",
                12 => "SODIMM",
                13 => "SRIMM",
                14 => "FB-DIMM",
                15 => "Die",
                _ => "-"
            };
        }

        public static string FormatBitsWidth(int totalWidthBits, int dataWidthBits) => $"{totalWidthBits} / {dataWidthBits} bit";

        public static string FormatRank(uint rank) => rank > 0 ? $"Rank {rank}" : "-";

        public static string FormatMillivolts(uint millivolts) => millivolts > 0 ? $"{millivolts / 1000.0:0.##} V" : "-";

        // combined; x:Bind cannot mix several calls with literal text in one attribute
        public static string FormatVoltageRange(uint configuredMillivolts, uint minMillivolts, uint maxMillivolts)
        {
            return $"{FormatMillivolts(configuredMillivolts)} / {FormatMillivolts(minMillivolts)} / {FormatMillivolts(maxMillivolts)}";
        }

        // combined, like FormatVoltageRange
        public static string FormatSpeedPair(uint configuredSpeedMhz, uint ratedSpeedMhz) =>
            $"{FormatMemorySpeed(configuredSpeedMhz)} / {FormatMemorySpeed(ratedSpeedMhz)}";


        // === storage ===

        public static string FormatCelsius(uint? celsius) => celsius.HasValue ? $"{celsius} °C" : "-";
        public static string FormatHours(uint? hours) => hours.HasValue ? $"{hours} h" : "-";
        public static string FormatPercent(uint? percent) => percent.HasValue ? $"{percent}%" : "-";

        // "-" when none is reported, a "?" only for the missing parts otherwise
        public static string FormatErrorCounts(ulong? total, ulong? corrected, ulong? uncorrected)
        {
            if (!total.HasValue && !corrected.HasValue && !uncorrected.HasValue) return "-";
            return $"{FormatCountOrUnknown(total)} total, {FormatCountOrUnknown(corrected)} corrected, {FormatCountOrUnknown(uncorrected)} uncorrected";
        }

        private static string FormatCountOrUnknown(ulong? value) => value?.ToString() ?? "?";

        public static string FormatCycleCount(uint? count, uint? max)
        {
            if (!count.HasValue) return "-";
            return max.HasValue && max.Value > 0 ? $"{count} / {max} max" : count.Value.ToString();
        }

        public static string FormatLatencyTriple(ulong? readMs, ulong? writeMs, ulong? flushMs)
        {
            if (!readMs.HasValue && !writeMs.HasValue && !flushMs.HasValue) return "-";
            return $"{FormatCountOrUnknown(readMs)} / {FormatCountOrUnknown(writeMs)} / {FormatCountOrUnknown(flushMs)} ms";
        }


        // === network ===

        // NetworkInterface.Speed is in bit/s
        public static string FormatBitsPerSecond(long bitsPerSecond)
        {
            if (bitsPerSecond <= 0) return "-";

            double gbps = bitsPerSecond / 1_000_000_000.0;
            if (gbps >= 1) return $"{gbps:0.#} Gbps";

            double mbps = bitsPerSecond / 1_000_000.0;
            return $"{mbps:0.#} Mbps";
        }

        // "A1B2C3D4E5F6" with colons
        public static string FormatMacAddress(string rawAddress)
        {
            if (string.IsNullOrEmpty(rawAddress) || rawAddress.Length != 12) return rawAddress ?? "-";

            var parts = new string[6];
            for (int i = 0; i < 6; i++)
            {
                parts[i] = rawAddress.Substring(i * 2, 2);
            }
            return string.Join(":", parts);
        }

        public static string FormatIpAddresses(IReadOnlyList<string> ipAddresses)
        {
            if (ipAddresses == null || ipAddresses.Count == 0) return "-";
            return string.Join(", ", ipAddresses);
        }

        // the enum name reads fine, except Wireless80211
        public static string FormatInterfaceType(NetworkInterfaceType type)
        {
            return type == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : type.ToString();
        }
    }
}
