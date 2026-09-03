using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace FluentSensors.Core.StaticInfo
{
    // shared, static formatting helpers for raw WinStaticInfoService facts, used by the Performance pages
    // per-hardware info panels
    public static class HardwareInfoFormatter
    {
        // === Shared ===
        // generic unit formatting with no single hardware type owning it

        public static string FormatYesNo(bool value) => value ? "Yes" : "No";

        public static string FormatMhz(uint speedMhz) => $"{speedMhz} MHz";

        public static string FormatBytesAsGb(ulong bytes)
        {
            double gb = bytes / 1024.0 / 1024.0 / 1024.0;
            return $"{gb:0.#} GB";
        }


        // === CPU ===

        // sums every distinct Win32_CacheMemory entry at the given Level; "distinct" (by Level+CacheType+Size)
        // is what correctly separates two genuinely different physical caches (e.g. P-Core L1 vs E-Core L1,
        // which differ in size) from the same shared cache reported more than once (e.g. L3, which came back
        // twice with an identical size on a real system; once per core group its associated with)
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

        // Win32_CacheMemory.CacheType is a numeric enum
        // Unlike Level (see WinCpuCacheEntry) this mapping is confirmed by Microsofts own SMBIOS-sourced
        // documentation
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


        // === GPU ===

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


        // === RAM ===

        // maps the raw SMBIOS "Memory Device Type" code (Win32_PhysicalMemory.SMBIOSMemoryType) to a readable
        // label
        // Only values realistically seen on consumer/workstation hardware are named, anything else falls
        // back to a raw numeric label instead of guessing
        // source: DMTF SMBIOS spec, Memory Device structure, Type field
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

        // SMBIOS/WMI name these fields "...ClockSpeedMhz", but the reported number is actually the DDR effective
        // transfer rate (MT/s), not the real clock frequency;
        // MT/s is 2x the real MHz for double data rate memory
        public static string FormatMemorySpeed(uint speedMts) => $"{speedMts} MT/s";

        // --- workaround: Win32_PhysicalMemory.FormFactor off-by-one vs SMBIOS spec ---
        // problem: the DMTF SMBIOS spec table starts at 1=Other, but Microsofts WMI/CIM provider reindexes it
        // internally and reports one lower per value (0=Other instead of 1=Other, and so on); the spec-numbered
        // table produced wrong labels in practice (12 read as "RIMM" on a system where CPU-Zs SPD tab, which
        // reads the modules SPD chip directly rather than going through SMBIOS/WMI at all, confirmed "SO-DIMM";
        // a second, independent real dump elsewhere also showed 8 on an actual desktop DIMM)
        // no public issue found for this exact WMI/CIM provider behavior
        // fix: shift the DMTF list down by one; verified against those two real systems rather than trusted
        // from the spec document alone
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

        // combined because x:Bind cannot mix multiple function calls with literal separator text in one attribute
        public static string FormatVoltageRange(uint configuredMillivolts, uint minMillivolts, uint maxMillivolts)
        {
            return $"{FormatMillivolts(configuredMillivolts)} / {FormatMillivolts(minMillivolts)} / {FormatMillivolts(maxMillivolts)}";
        }

        // combined because x:Bind cannot mix multiple function calls with literal separator text in one attribute
        // (same reasoning as FormatVoltageRange above)
        public static string FormatSpeedPair(uint configuredSpeedMhz, uint ratedSpeedMhz) =>
            $"{FormatMemorySpeed(configuredSpeedMhz)} / {FormatMemorySpeed(ratedSpeedMhz)}";


        // === Storage ===

        public static string FormatCelsius(uint? celsius) => celsius.HasValue ? $"{celsius} °C" : "-";
        public static string FormatHours(uint? hours) => hours.HasValue ? $"{hours} h" : "-";
        public static string FormatPercent(uint? percent) => percent.HasValue ? $"{percent}%" : "-";

        // if all three are unreported, shows one plain "-" instead of a noisy "?, ?, ?"; if only some are
        // unreported (e.g. Total known, Corrected not), shows "?" for just those parts instead of hiding the
        // whole line for a single missing field
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


        // === Network ===

        // NetworkInterface.Speed is bits/second; picks Gbps or Mbps
        public static string FormatBitsPerSecond(long bitsPerSecond)
        {
            if (bitsPerSecond <= 0) return "-";

            double gbps = bitsPerSecond / 1_000_000_000.0;
            if (gbps >= 1) return $"{gbps:0.#} Gbps";

            double mbps = bitsPerSecond / 1_000_000.0;
            return $"{mbps:0.#} Mbps";
        }

        // NetworkInterface.GetPhysicalAddress().ToString() returns a bare 12-character hex string (e.g.
        // "A1B2C3D4E5F6"); this inserts the conventional colon separators for display
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

        // NetworkInterfaceType.ToString() is already readable for most values (Ethernet, GigabitEthernet, ...);
        // Wireless80211 is the one exception worth a friendlier label
        public static string FormatInterfaceType(NetworkInterfaceType type)
        {
            return type == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : type.ToString();
        }
    }
}
