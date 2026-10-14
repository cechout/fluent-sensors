using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

using FluentSensors.Common.Localization;
using FluentSensors.Common.Sensors;

namespace FluentSensors.Core.StaticInfo
{
    // formatting of the WinStaticInfoService facts for the hardware view info panels
    public static class HardwareInfoFormatter
    {
        // === shared ===

        public static string FormatYesNo(bool value) => AppStrings.Get(value ? "Info_Yes" : "Info_No");

        public static string FormatMhz(uint speedMhz) => $"{speedMhz} MHz";

        public static string FormatBytesAsGb(ulong bytes)
        {
            double gb = bytes / 1024.0 / 1024.0 / 1024.0;
            return $"{gb:0.#} GB";
        }

        public static string FormatList(IReadOnlyList<string> items) =>
            items == null || items.Count == 0 ? "-" : string.Join(", ", items);

        public static string FormatTextOrDash(string text) => string.IsNullOrWhiteSpace(text) ? "-" : text;

        // "PCIe 4.0 x8"; the speed is the generation, see WinPciLink
        public static string FormatPciLink(uint speed, uint width) => speed > 0 && width > 0 ? $"PCIe {speed}.0 x{width}" : "-";

        // current and maximum combined, like FormatVoltageRange
        public static string FormatPciLinkPair(WinPciLink? link) =>
            link != null
                ? $"{FormatPciLink(link.CurrentSpeed, link.CurrentWidth)} / {FormatPciLink(link.MaxSpeed, link.MaxWidth)}"
                : "-";


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

        // "Intel64 Family 6 Model 154 Stepping 3" as "6 / 154 / 3"; decimal, as Windows writes it
        public static string FormatCpuSignature(string description)
        {
            var match = Regex.Match(description ?? "", @"Family (\d+) Model (\d+) Stepping (\d+)");
            return match.Success ? $"{match.Groups[1].Value} / {match.Groups[2].Value} / {match.Groups[3].Value}" : "-";
        }

        public static string FormatMicrocode(uint? revision) => revision.HasValue ? $"0x{revision.Value:X}" : "-";

        // Win32_CacheMemory.CacheType; unlike Level (see WinCpuCacheEntry) documented by Microsoft
        public static string FormatCacheType(uint cacheType)
        {
            return cacheType switch
            {
                1 => AppStrings.Get("Info_Other"),
                2 => AppStrings.Get("Info_Unknown"),
                3 => AppTerms.Get("Info_CacheInstruction"),
                4 => AppTerms.Get("Info_CacheData"),
                5 => AppTerms.Get("Info_CacheUnified"),
                _ => AppStrings.Format("Info_UnknownValue", cacheType)
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
                _ => AppStrings.Format("Info_UnknownValue", $"0x{vendorId:X4}")
            };
        }

        // the maker of the card or laptop, from the subsystem vendor in the PnP id ("SUBSYS_1C621043", the last four
        // digits); PCI-SIG vendor IDs, the common graphics board partners only
        public static string FormatBoardVendor(string pnpDeviceId)
        {
            var match = Regex.Match(pnpDeviceId ?? "", "SUBSYS_[0-9A-Fa-f]{4}([0-9A-Fa-f]{4})");
            if (!match.Success) return "-";

            uint vendorId = System.Convert.ToUInt32(match.Groups[1].Value, 16);
            return vendorId switch
            {
                0x1002 => "AMD",
                0x1025 => "Acer",
                0x1028 => "Dell",
                0x103C => "HP",
                0x1043 => "ASUS",
                0x10B0 => "Gainward",
                0x10DE => "NVIDIA",
                0x1414 => "Microsoft",
                0x144D => "Samsung",
                0x1458 => "Gigabyte",
                0x1462 => "MSI",
                0x148C => "PowerColor",
                0x1558 => "Clevo",
                0x1569 => "Palit",
                0x1682 => "XFX",
                0x17AA => "Lenovo",
                0x1849 => "ASRock",
                0x196E => "PNY",
                0x19DA => "Zotac",
                0x1B4C => "KFA2 / Galax",
                0x1D05 => "Tongfang",
                0x1DA2 => "Sapphire",
                0x3842 => "EVGA",
                0x7377 => "Colorful",
                0x8086 => "Intel",
                _ => AppStrings.Format("Info_UnknownValue", $"0x{vendorId:X4}")
            };
        }

        // where the picture goes: the displays it drives, the integrated gpu a hybrid discrete one renders through, or
        // none; "" when the gpu had no DXGI match and so no known adapter
        public static string FormatGpuDisplay(WinGpuInfo gpu)
        {
            if (gpu.AdapterLuid == 0) return "";

            if (gpu.Displays.Count > 0)
            {
                return string.Join(", ", gpu.Displays.Select(d =>
                    d.IsBuiltIn ? AppStrings.Get("Info_DisplayBuiltIn")
                    : d.FriendlyName.Length > 0 ? d.FriendlyName
                    : AppStrings.Get("Info_DisplayExternal")));
            }

            return gpu.DisplayViaGpuName != null
                ? AppStrings.Format("Info_DisplayVia", gpu.DisplayViaGpuName)
                : AppStrings.Get("Info_DisplayNone");
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
                _ => AppStrings.Format("Info_UnknownValue", smbiosType)
            };
        }

        // the SMBIOS Physical Memory Array "Memory Error Correction" (DMTF SMBIOS spec, Type 16)
        public static string FormatErrorCorrection(uint errorCorrection)
        {
            return errorCorrection switch
            {
                1 => AppStrings.Get("Info_Other"),
                2 => AppStrings.Get("Info_Unknown"),
                3 => AppTerms.Get("Info_EccNone"),
                4 => AppTerms.Get("Info_EccParity"),
                5 => AppTerms.Get("Info_EccSingleBit"),
                6 => AppTerms.Get("Info_EccMultiBit"),
                7 => "CRC",
                _ => "-"
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
                0 => AppStrings.Get("Info_Other"),
                1 => AppStrings.Get("Info_Unknown"),
                2 => "SIMM",
                3 => "SIP",
                4 => AppTerms.Get("Info_FormChip"),
                5 => "DIP",
                6 => "ZIP",
                7 => AppTerms.Get("Info_FormProprietaryCard"),
                8 => "DIMM",
                9 => "TSOP",
                10 => AppTerms.Get("Info_FormRowOfChips"),
                11 => "RIMM",
                12 => "SODIMM",
                13 => "SRIMM",
                14 => "FB-DIMM",
                15 => AppTerms.Get("Info_FormDie"),
                _ => "-"
            };
        }

        public static string FormatBitsWidth(int totalWidthBits, int dataWidthBits) => $"{totalWidthBits} / {dataWidthBits} bit";

        public static string FormatRank(uint rank) => rank > 0 ? AppTerms.Format("Info_Rank", rank) : "-";

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
            return AppStrings.Format("Info_ErrorCounts", FormatCountOrUnknown(total), FormatCountOrUnknown(corrected), FormatCountOrUnknown(uncorrected));
        }

        private static string FormatCountOrUnknown(ulong? value) => value?.ToString() ?? "?";

        public static string FormatCycleCount(uint? count, uint? max)
        {
            if (!count.HasValue) return "-";
            return max.HasValue && max.Value > 0 ? AppStrings.Format("Info_CycleCount", count, max) : count.Value.ToString();
        }

        // MSFT_PhysicalDisk.MediaType, with the spindle speed of a drive that has platters
        public static string FormatMediaType(uint? mediaType, uint? spindleSpeedRpm)
        {
            string type = mediaType switch
            {
                3 => "HDD",
                4 => "SSD",
                5 => "SCM",
                _ => "-"
            };

            return type != "-" && spindleSpeedRpm > 0 ? $"{type}, {spindleSpeedRpm} rpm" : type;
        }

        // MSFT_PhysicalDisk.HealthStatus
        public static string FormatHealthStatus(uint? healthStatus)
        {
            return healthStatus switch
            {
                0 => AppTerms.Get("Info_HealthHealthy"),
                1 => AppTerms.Get("Info_HealthWarning"),
                2 => AppTerms.Get("Info_HealthUnhealthy"),
                5 => AppStrings.Get("Info_Unknown"),
                _ => "-"
            };
        }

        // MSFT_Disk.PartitionStyle; 0 is a disk without a partition table
        public static string FormatPartitionStyle(uint? partitionStyle)
        {
            return partitionStyle switch
            {
                1 => "MBR",
                2 => "GPT",
                _ => "-"
            };
        }

        // what Windows starts from on this disk
        public static string FormatDiskRole(bool isBootDisk, bool isSystemDisk)
        {
            var roles = new List<string>();
            if (isBootDisk) roles.Add(AppTerms.Get("Info_DiskRoleBoot"));
            if (isSystemDisk) roles.Add(AppTerms.Get("Info_DiskRoleSystem"));
            return FormatList(roles);
        }

        // one line per volume with a drive letter
        public static string FormatVolumes(IReadOnlyList<WinVolumeInfo> volumes)
        {
            if (volumes == null || volumes.Count == 0) return "-";

            return string.Join("\n", volumes.Select(v =>
                AppStrings.Format("Info_Volume", v.Letter, v.FileSystem, FormatBytesAsGb(v.SizeBytes), FormatBytesAsGb(v.FreeBytes))));
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

        // DOT11_PHY_TYPE with the marketing name; 6E is ax on the 6 GHz band
        public static string FormatWlanStandard(uint phyType, uint frequencyMhz)
        {
            return phyType switch
            {
                4 => "802.11a",
                5 => "802.11b",
                6 => "802.11g",
                7 => "Wi-Fi 4 (802.11n)",
                8 => "Wi-Fi 5 (802.11ac)",
                9 => "802.11ad",
                10 => frequencyMhz >= SixGhzBandStartMhz ? "Wi-Fi 6E (802.11ax)" : "Wi-Fi 6 (802.11ax)",
                11 => "Wi-Fi 7 (802.11be)",
                _ => AppStrings.Format("Info_UnknownValue", phyType)
            };
        }

        private const uint SixGhzBandStartMhz = 5925;

        // from the channel center frequency; without one (the access point was not in the last scan) from the channel
        // number, which cannot tell 6 GHz apart and so only answers for the two older bands
        public static string FormatWlanBand(uint frequencyMhz, uint channel)
        {
            if (frequencyMhz == 0)
            {
                if (channel == 0) return "-";
                return channel <= 14 ? "2.4 GHz" : "5 GHz";
            }

            if (frequencyMhz < 3000) return "2.4 GHz";
            if (frequencyMhz < SixGhzBandStartMhz) return "5 GHz";
            return frequencyMhz < 7200 ? "6 GHz" : "60 GHz";
        }

        public static string FormatWlanChannel(uint channel) => channel > 0 ? channel.ToString() : "-";

        // DOT11_AUTH_ALGORITHM
        public static string FormatWlanSecurity(uint authAlgorithm)
        {
            return authAlgorithm switch
            {
                1 => AppTerms.Get("Info_WlanOpen"),
                2 => "WEP",
                3 => "WPA-Enterprise",
                4 => "WPA-Personal",
                6 => "WPA2-Enterprise",
                7 => "WPA2-Personal",
                8 => "WPA3-Enterprise (192-bit)",
                9 => "WPA3-Personal",
                10 => "OWE",
                11 => "WPA3-Enterprise",
                _ => AppStrings.Format("Info_UnknownValue", authAlgorithm)
            };
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


        // === battery ===

        // the four characters of BATTERY_INFORMATION.Chemistry; an unknown code stays as the firmware wrote it
        public static string FormatBatteryChemistry(string chemistry)
        {
            return (chemistry ?? "").ToUpperInvariant() switch
            {
                "" => "-",
                "LION" => "Li-ion",
                "LIP" or "LIPO" => "Li-polymer",
                "NIMH" => "NiMH",
                "NICD" => "NiCd",
                "NIZN" => "NiZn",
                "PBAC" => AppTerms.Get("Info_ChemistryLeadAcid"),
                "RAM" => AppTerms.Get("Info_ChemistryAlkaline"),
                _ => chemistry
            };
        }

        public static string FormatMilliwattHours(uint? milliwattHours) =>
            milliwattHours.HasValue ? $"{milliwattHours.Value / 1000.0:0.#} Wh" : "-";

        public static string FormatCount(uint? count) => count?.ToString() ?? "-";


        // === display ===

        // DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY; USB-C alt mode and Thunderbolt show as DisplayPort
        public static string FormatDisplayConnector(uint outputTechnology)
        {
            return outputTechnology switch
            {
                0 => "VGA",
                4 => "DVI",
                5 => "HDMI",
                6 => "LVDS",
                10 => "DisplayPort",
                11 => "eDP",
                15 => "Miracast",
                16 or 17 => AppTerms.Get("Display_ConnectorIndirect"),
                0x80000000 => AppTerms.Get("Display_ConnectorInternal"),
                _ => "-"
            };
        }

        public static string FormatResolution(int widthPixels, int heightPixels) =>
            widthPixels > 0 && heightPixels > 0 ? $"{widthPixels} x {heightPixels}" : "-";

        // a whole number without decimals, 59.94 with them
        public static string FormatRefreshRate(double hertz) => hertz > 0 ? $"{hertz:0.##} Hz" : "-";

        // with the time one frame stays up, "165.04 Hz (6.06 ms)"
        public static string FormatRefreshRateWithFrameTime(double hertz) =>
            hertz > 0 ? $"{hertz:0.##} Hz ({1000.0 / hertz:0.##} ms)" : "-";

        // red, green and blue at the color depth
        public static string FormatBitsPerPixel(int bitsPerColorChannel) =>
            bitsPerColorChannel > 0 ? AppTerms.Format("Display_Bits", bitsPerColorChannel * ColorChannelCount) : "-";

        // one frame as it leaves the gpu, "11.7 MB"
        public static string FormatFrameSize(int widthPixels, int heightPixels, int bitsPerColorChannel)
        {
            double bytes = FrameBytes(widthPixels, heightPixels, bitsPerColorChannel);
            return bytes > 0 ? $"{bytes / BytesPerMebibyte:0.#} MB" : "-";
        }

        // the frames of one second, in the data speed unit of the settings: "1.89 GB/s" or "16.2 Gbit/s"
        public static string FormatFrameDataRate(int widthPixels, int heightPixels, int bitsPerColorChannel, double hertz)
        {
            double bytes = FrameBytes(widthPixels, heightPixels, bitsPerColorChannel);
            if (bytes <= 0 || hertz <= 0) return "-";

            var (value, unit) = SensorUnitFormatter.Scale(bytes * hertz / BytesPerMebibyte, "Throughput");

            // three significant digits
            string format = value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
            return $"{value.ToString(format)} {unit}";
        }

        private const int ColorChannelCount = 3;
        private const double BytesPerMebibyte = 1_048_576.0;

        // the visible pixels only: no blanking interval, no compression, no chroma subsampling
        private static double FrameBytes(int widthPixels, int heightPixels, int bitsPerColorChannel)
        {
            if (widthPixels <= 0 || heightPixels <= 0 || bitsPerColorChannel <= 0) return 0;
            return (double)widthPixels * heightPixels * bitsPerColorChannel * ColorChannelCount / 8;
        }

        // the diagonal in inches, then the visible area
        public static string FormatDisplaySize(int? widthCentimeters, int? heightCentimeters)
        {
            if (widthCentimeters is not int width || heightCentimeters is not int height) return "-";

            double diagonalInches = System.Math.Sqrt(width * width + height * height) / 2.54;
            return $"{diagonalInches:0.#}\" ({width} x {height} cm)";
        }

        public static string FormatHdr(bool isSupported, bool isEnabled)
        {
            if (!isSupported) return AppTerms.Get("Display_HdrNotSupported");
            return AppTerms.Get(isEnabled ? "Display_HdrOn" : "Display_HdrOff");
        }

        public static string FormatColorDepth(int bitsPerColorChannel) =>
            bitsPerColorChannel > 0 ? AppTerms.Format("Display_BitsPerChannel", bitsPerColorChannel) : "-";

        // "2021-W07", the year alone when the EDID has no week
        public static string FormatManufactureDate(int? year, int? week)
        {
            if (!year.HasValue) return "-";
            return week.HasValue ? $"{year}-W{week:00}" : year.ToString();
        }

        // DISPLAYCONFIG_ROTATION
        public static string FormatDisplayOrientation(uint rotation)
        {
            return rotation switch
            {
                1 => AppTerms.Get("Display_OrientationLandscape"),
                2 => AppTerms.Get("Display_OrientationPortrait"),
                3 => AppTerms.Get("Display_OrientationLandscapeFlipped"),
                4 => AppTerms.Get("Display_OrientationPortraitFlipped"),
                _ => "-"
            };
        }

        public static string FormatScale(int scalePercent) => scalePercent > 0 ? $"{scalePercent} %" : "-";

        // "60 / 120 / 165 Hz"
        public static string FormatRefreshRates(IReadOnlyList<int>? ratesHz) =>
            ratesHz is { Count: > 0 } ? $"{string.Join(" / ", ratesHz)} Hz" : "-";

        // whole nits above ten, the black level keeps its decimals
        public static string FormatNits(double? nits)
        {
            if (!nits.HasValue) return "-";
            return nits.Value >= 10 ? $"{nits.Value:0} nits" : $"{nits.Value:0.###} nits";
        }

        public static string FormatGamma(double? gamma) => gamma.HasValue ? $"{gamma.Value:0.0#}" : "-";

        // from the native resolution and the visible area
        public static string FormatPixelDensity(int nativeWidthPixels, int nativeHeightPixels, int? widthCentimeters, int? heightCentimeters)
        {
            if (nativeWidthPixels <= 0 || nativeHeightPixels <= 0) return "-";
            if (widthCentimeters is not int width || heightCentimeters is not int height || width <= 0 || height <= 0) return "-";

            double diagonalPixels = System.Math.Sqrt((double)nativeWidthPixels * nativeWidthPixels + (double)nativeHeightPixels * nativeHeightPixels);
            double diagonalInches = System.Math.Sqrt(width * width + height * height) / 2.54;
            return $"{diagonalPixels / diagonalInches:0} ppi";
        }

        // VCP code D6 of the Monitor Control Command Set
        public static string FormatDdcPowerMode(uint? powerMode)
        {
            return powerMode switch
            {
                null => "-",
                1 => AppTerms.Get("Display_PowerOn"),
                2 => AppTerms.Get("Display_PowerStandby"),
                3 => AppTerms.Get("Display_PowerSuspend"),
                4 or 5 => AppTerms.Get("Display_PowerOff"),
                _ => AppStrings.Format("Info_UnknownValue", powerMode)
            };
        }

        // VCP code 60; the standard values, a vendor value (USB-C on many monitors) shows as its number
        public static string FormatDdcInputSource(uint? inputSource)
        {
            return inputSource switch
            {
                null => "-",
                1 => "VGA 1",
                2 => "VGA 2",
                3 => "DVI 1",
                4 => "DVI 2",
                5 => "Composite 1",
                6 => "Composite 2",
                7 => "S-Video 1",
                8 => "S-Video 2",
                12 => "Component 1",
                13 => "Component 2",
                14 => "Component 3",
                15 => "DisplayPort 1",
                16 => "DisplayPort 2",
                17 => "HDMI 1",
                18 => "HDMI 2",
                _ => $"0x{inputSource:X2}"
            };
        }

        // VCP code B6
        public static string FormatDdcTechnology(uint? technology)
        {
            return technology switch
            {
                null => "-",
                1 or 2 => "CRT",
                3 => "LCD",
                4 => "LCoS",
                5 => "Plasma",
                6 => "OLED",
                7 => "EL",
                _ => $"0x{technology:X2}"
            };
        }

        public static string FormatVersionOrDash(string version) => string.IsNullOrEmpty(version) ? "-" : version;


        // === motherboard ===

        // the SMBIOS System Enclosure "Type" (DMTF SMBIOS spec, Type 3); the cases a user would name, "" for the rest
        public static string FormatChassisType(uint chassisType)
        {
            return chassisType switch
            {
                3 => AppTerms.Get("Info_ChassisDesktop"),
                4 => AppTerms.Get("Info_ChassisLowProfileDesktop"),
                6 => AppTerms.Get("Info_ChassisMiniTower"),
                7 => AppTerms.Get("Info_ChassisTower"),
                9 => AppTerms.Get("Info_ChassisLaptop"),
                10 => AppTerms.Get("Info_ChassisNotebook"),
                13 => AppTerms.Get("Info_ChassisAllInOne"),
                30 => AppTerms.Get("Info_ChassisTablet"),
                31 => AppTerms.Get("Info_ChassisConvertible"),
                32 => AppTerms.Get("Info_ChassisDetachable"),
                35 => AppTerms.Get("Info_ChassisMiniPc"),
                _ => ""
            };
        }
    }
}
