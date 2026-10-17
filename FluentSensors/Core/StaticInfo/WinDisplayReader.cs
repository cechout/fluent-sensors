using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Vortice.DXGI;


namespace FluentSensors.Core.StaticInfo
{
    // the active displays with their mode, connector, HDR state, EDID facts and what the monitor says over DDC/CI:
    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig
    // the display config part is cheap (well below a millisecond) and is all the sensor source asks for; the details
    // (EDID over WMI, DXGI, the mode list, DDC/CI) cost up to a few hundred milliseconds and are only asked for the facts
    // apart from WinDisplayTopology on purpose: that one runs before WinUI starts and answers a narrower question
    public static partial class WinDisplayReader
    {
        // === fields ===

        private const uint QdcOnlyActivePaths = 0x2;
        private const int PathInfoSize = 72; // DISPLAYCONFIG_PATH_INFO
        private const int ModeInfoSize = 64; // DISPLAYCONFIG_MODE_INFO
        private const int PathSourceAdapterOffset = 0; // sourceInfo.adapterId, then sourceInfo.id
        private const int PathSourceModeIndexOffset = 12; // sourceInfo.modeInfoIdx
        private const int PathTargetAdapterOffset = 20; // targetInfo.adapterId, then targetInfo.id
        private const int PathTargetTechnologyOffset = 36; // targetInfo.outputTechnology
        private const int PathTargetRotationOffset = 40; // targetInfo.rotation
        private const int PathTargetRefreshRateOffset = 48; // targetInfo.refreshRate
        private const uint ModeIndexInvalid = 0xFFFFFFFF;

        // DISPLAYCONFIG_MODE_INFO: type, id, adapter, then the source mode (width, height, pixel format, position)
        private const uint ModeInfoTypeSource = 1;
        private const int ModeSourceWidthOffset = 16;
        private const int ModeSourceHeightOffset = 20;
        private const int ModeSourcePositionOffset = 28;

        // DISPLAYCONFIG_SOURCE_DEVICE_NAME: the header, then the GDI name
        private const int SourceNameType = 1; // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
        private const int SourceNameSize = 84;
        private const int SourceNameGdiNameOffset = 20;
        private const int SourceNameGdiNameChars = 32;

        // DISPLAYCONFIG_TARGET_DEVICE_NAME: the header, flags, technology, edid ids, connector, then the names
        private const int TargetNameType = 2; // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
        private const int TargetNameSize = 420;
        private const int TargetNameFlagsOffset = 20;
        private const uint TargetNameEdidIdsValid = 1u << 2;
        private const int TargetNameManufactureIdOffset = 28;
        private const int TargetNameProductCodeOffset = 30;
        private const int TargetNameFriendlyNameOffset = 36;
        private const int TargetNameFriendlyNameChars = 64;
        private const int TargetNameDevicePathOffset = 164;
        private const int TargetNameDevicePathChars = 128;

        // DISPLAYCONFIG_TARGET_PREFERRED_MODE: the header, width, height, then the signal of that mode
        private const int PreferredModeType = 3; // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_PREFERRED_MODE
        private const int PreferredModeSize = 80;
        private const int PreferredModeWidthOffset = 20;
        private const int PreferredModeHeightOffset = 24;

        // DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO: the header, a bit field, the color encoding, the bits per channel
        private const int AdvancedColorType = 9; // DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO
        private const int AdvancedColorSize = 32;
        private const int AdvancedColorValueOffset = 20;
        private const int AdvancedColorBitsOffset = 28;
        private const uint AdvancedColorSupported = 1u << 0;
        private const uint AdvancedColorEnabled = 1u << 1;

        // DISPLAYCONFIG_SDR_WHITE_LEVEL: the header, then the level, where 1000 stands for 80 nits
        private const int SdrWhiteLevelType = 11; // DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL
        private const int SdrWhiteLevelSize = 24;
        private const int SdrWhiteLevelValueOffset = 20;

        // DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY values of a panel inside the device
        private const uint OutputTechnologyInternal = 0x80000000;
        private const uint OutputTechnologyDisplayPortEmbedded = 11;
        private const uint OutputTechnologyUdiEmbedded = 13;

        private const uint MonitorDefaultToNull = 0;
        private const int MdtEffectiveDpi = 0;

        // PHYSICAL_MONITOR, packed: the handle, then a description of 128 characters
        private const int PhysicalMonitorSize = 264;

        // VCP codes of the VESA Monitor Control Command Set
        public const byte VcpBrightness = 0x10;
        public const byte VcpContrast = 0x12;
        public const byte VcpVolume = 0x62;
        private const byte VcpInputSource = 0x60;
        private const byte VcpTechnology = 0xB6;
        private const byte VcpUsageHours = 0xC0;
        private const byte VcpFirmwareLevel = 0xC9;
        private const byte VcpPowerMode = 0xD6;
        private const byte VcpMccsVersion = 0xDF;

        // a code a monitor did not answer this often in a row is left alone for a while; an unanswered request blocks
        // for its timeout, and some monitors flicker or drop their OSD while they are asked
        private const int DdcFailureLimit = 3;
        private static readonly TimeSpan DdcRetryInterval = TimeSpan.FromMinutes(5);

        // the DDC/CI facts move slowly (usage hours) or only by hand (input); asked again after this long
        private static readonly TimeSpan DdcFactsMaxAge = TimeSpan.FromSeconds(60);

        private const string BuiltInDisplayName = "Built-in Display";
        private const string UnnamedDisplayName = "Display";

        // one I2C request at a time, whoever asks (the sensor source and the facts run on different threads)
        private static readonly object _ddcLock = new();

        // under _ddcLock: failures in a row and the time of the last one, per display key and code; the facts per key
        private static readonly Dictionary<(string Key, byte Code), (int Failures, long Timestamp)> _ddcFailures = new();
        private static readonly Dictionary<string, (WinDisplayDdcInfo? Info, long Timestamp)> _ddcFacts = new();

        // the EDID facts of one monitor, by the upper case InstanceName of its root\wmi objects
        private record EdidInfo(string InstanceName, int? Year, int? Week, int? WidthCentimeters, int? HeightCentimeters, double? Gamma);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }


        // === public api ===

        // empty when the display config cannot be read
        // includeDetails adds what is slow: the EDID facts, the luminance, the mode list, the scale and DDC/CI
        public static List<WinDisplayInfo> QueryDisplays(bool includeDetails)
        {
            var result = new List<WinDisplayInfo>();

            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != 0) return result;

            var paths = new byte[pathCount * PathInfoSize];
            var modes = new byte[modeCount * ModeInfoSize];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return result;

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedKeys = new HashSet<string>();

            for (int i = 0; i < pathCount; i++)
            {
                int path = i * PathInfoSize;
                uint technology = BitConverter.ToUInt32(paths, path + PathTargetTechnologyOffset);
                uint numerator = BitConverter.ToUInt32(paths, path + PathTargetRefreshRateOffset);
                uint denominator = BitConverter.ToUInt32(paths, path + PathTargetRefreshRateOffset + 4);
                bool isBuiltIn = technology == OutputTechnologyInternal
                    || technology == OutputTechnologyDisplayPortEmbedded
                    || technology == OutputTechnologyUdiEmbedded;

                // the desktop mode of the source this display shows
                int width = 0, height = 0, positionX = 0, positionY = 0;
                bool hasSourceMode = false;
                uint modeIndex = BitConverter.ToUInt32(paths, path + PathSourceModeIndexOffset);
                if (modeIndex != ModeIndexInvalid && modeIndex < modeCount)
                {
                    int mode = (int)modeIndex * ModeInfoSize;
                    if (BitConverter.ToUInt32(modes, mode) == ModeInfoTypeSource)
                    {
                        hasSourceMode = true;
                        width = (int)BitConverter.ToUInt32(modes, mode + ModeSourceWidthOffset);
                        height = (int)BitConverter.ToUInt32(modes, mode + ModeSourceHeightOffset);
                        positionX = BitConverter.ToInt32(modes, mode + ModeSourcePositionOffset);
                        positionY = BitConverter.ToInt32(modes, mode + ModeSourcePositionOffset + 4);
                    }
                }

                var target = QueryTargetName(paths, path + PathTargetAdapterOffset);
                var (isHdrSupported, isHdrEnabled, bitsPerChannel) = QueryAdvancedColor(paths, path + PathTargetAdapterOffset);

                // a second monitor of the same model keeps its own name and key
                string baseName = target.FriendlyName.Length > 0 ? target.FriendlyName : isBuiltIn ? BuiltInDisplayName : UnnamedDisplayName;
                string name = MakeUnique(baseName, usedNames, " #");

                string baseKey = target.ManufacturerId.Length > 0
                    ? $"{target.ManufacturerId}{target.ProductCode}".ToLowerInvariant()
                    : isBuiltIn ? "builtin" : "display";
                string key = MakeUnique(baseKey, usedKeys, "-");

                var sourceName = QueryDeviceInfo(paths, path + PathSourceAdapterOffset, SourceNameType, SourceNameSize);

                result.Add(new WinDisplayInfo(
                    Name: name,
                    Key: key,
                    InstancePath: target.InstancePath,
                    GdiDeviceName: sourceName != null ? ReadString(sourceName, SourceNameGdiNameOffset, SourceNameGdiNameChars) : "",
                    AdapterLuid: ReadLuid(paths, path + PathSourceAdapterOffset),
                    IsBuiltIn: isBuiltIn,
                    IsPrimary: hasSourceMode && positionX == 0 && positionY == 0,
                    OutputTechnology: technology,
                    Rotation: BitConverter.ToUInt32(paths, path + PathTargetRotationOffset),
                    PositionX: positionX,
                    PositionY: positionY,
                    WidthPixels: width,
                    HeightPixels: height,
                    RefreshRateHz: denominator == 0 ? 0 : Math.Round((double)numerator / denominator, 2),
                    IsHdrSupported: isHdrSupported,
                    IsHdrEnabled: isHdrEnabled,
                    BitsPerColorChannel: bitsPerChannel,
                    ManufacturerId: target.ManufacturerId,
                    ProductCode: target.ProductCode));
            }

            if (!includeDetails) return result;

            var edid = QueryEdid();
            var luminance = QueryLuminance();

            for (int i = 0; i < result.Count; i++)
            {
                var display = result[i];
                int targetOffset = i * PathInfoSize + PathTargetAdapterOffset;

                var monitorEdid = display.InstancePath.Length > 0
                    ? edid.FirstOrDefault(e => e.InstanceName.StartsWith(display.InstancePath, StringComparison.Ordinal))
                    : null;
                var (nativeWidth, nativeHeight) = QueryPreferredMode(paths, targetOffset);
                luminance.TryGetValue(display.GdiDeviceName, out var nits);

                result[i] = display with
                {
                    NativeWidthPixels = nativeWidth,
                    NativeHeightPixels = nativeHeight,
                    ScalePercent = QueryScalePercent(display),
                    SdrWhiteLevelNits = display.IsHdrSupported && display.IsHdrEnabled ? QuerySdrWhiteLevel(paths, targetOffset) : null,
                    MaxLuminanceNits = nits.Max > 0 ? nits.Max : null,
                    MinLuminanceNits = nits.Max > 0 ? nits.Min : null,
                    ManufactureYear = monitorEdid?.Year,
                    ManufactureWeek = monitorEdid?.Week,
                    WidthCentimeters = monitorEdid?.WidthCentimeters,
                    HeightCentimeters = monitorEdid?.HeightCentimeters,
                    Gamma = monitorEdid?.Gamma,
                    Ddc = QueryDdcFacts(result, display)
                };
            }

            return result;
        }

        // brightness, contrast and speaker volume in percent, by VCP code; only what the monitor answers
        // KNOWN UNRELIABLE: DDC/CI depends on the monitor, the cable, the dock and the gpu driver; many monitors answer
        // slowly, some only part of the codes, some not at all, and a KVM switch or an adapter can swallow it
        // untested: written against the documentation, no external monitor was at hand
        public static Dictionary<byte, double> QueryDdcLevels(IReadOnlyList<WinDisplayInfo> displays, WinDisplayInfo display)
        {
            var result = new Dictionary<byte, double>();

            lock (_ddcLock)
            {
                WithPhysicalMonitor(displays, display, handle =>
                {
                    foreach (byte code in new[] { VcpBrightness, VcpContrast, VcpVolume })
                    {
                        // a continuous control answers its position and its maximum
                        if (TryReadVcp(display.Key, handle, code, out uint current, out uint maximum) && maximum > 0)
                        {
                            result[code] = Math.Clamp(current * 100.0 / maximum, 0, 100);
                        }
                    }
                });
            }

            return result;
        }

        // "DISPLAY\AUOC199\4&3861AF4C&0&UID8388688" from a monitor device path
        // ("\\?\DISPLAY#AUOC199#4&3861af4c&0&UID8388688#{e6f07b5f-...}"); the root\wmi InstanceName of that monitor is
        // this with an "_0" at the end
        public static string ToInstancePath(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return "";

            string path = devicePath.StartsWith(@"\\?\", StringComparison.Ordinal) ? devicePath[4..] : devicePath;
            int interfaceStart = path.IndexOf("#{", StringComparison.Ordinal);
            if (interfaceStart >= 0) path = path[..interfaceStart];

            return path.Replace('#', '\\').ToUpperInvariant();
        }


        // === display config ===

        private static string MakeUnique(string text, HashSet<string> used, string separator)
        {
            string unique = text;
            for (int number = 2; !used.Add(unique); number++)
            {
                unique = $"{text}{separator}{number}";
            }
            return unique;
        }

        private static long ReadLuid(byte[] buffer, int offset) =>
            (long)BitConverter.ToInt32(buffer, offset + 4) << 32 | BitConverter.ToUInt32(buffer, offset);

        // adapter and id sit in the path in the same order the request header wants them, for the source and the target
        private static unsafe byte[]? QueryDeviceInfo(byte[] paths, int adapterOffset, int type, int size)
        {
            var request = new byte[size];
            BitConverter.TryWriteBytes(request.AsSpan(0), type);
            BitConverter.TryWriteBytes(request.AsSpan(4), size);
            Array.Copy(paths, adapterOffset, request, 8, 12);

            fixed (byte* buffer = request)
            {
                return DisplayConfigGetDeviceInfo(buffer) == 0 ? request : null;
            }
        }

        private static (string FriendlyName, string ManufacturerId, string ProductCode, string InstancePath) QueryTargetName(byte[] paths, int targetOffset)
        {
            var data = QueryDeviceInfo(paths, targetOffset, TargetNameType, TargetNameSize);
            if (data == null) return ("", "", "", "");

            string friendlyName = ReadString(data, TargetNameFriendlyNameOffset, TargetNameFriendlyNameChars);
            string devicePath = ReadString(data, TargetNameDevicePathOffset, TargetNameDevicePathChars);

            string manufacturerId = "";
            string productCode = "";
            if ((BitConverter.ToUInt32(data, TargetNameFlagsOffset) & TargetNameEdidIdsValid) != 0)
            {
                manufacturerId = DecodeManufacturerId(BitConverter.ToUInt16(data, TargetNameManufactureIdOffset));
                if (manufacturerId.Length > 0) productCode = BitConverter.ToUInt16(data, TargetNameProductCodeOffset).ToString("X4");
            }

            return (friendlyName, manufacturerId, productCode, ToInstancePath(devicePath));
        }

        private static (bool IsSupported, bool IsEnabled, int BitsPerChannel) QueryAdvancedColor(byte[] paths, int targetOffset)
        {
            var data = QueryDeviceInfo(paths, targetOffset, AdvancedColorType, AdvancedColorSize);
            if (data == null) return (false, false, 0);

            uint value = BitConverter.ToUInt32(data, AdvancedColorValueOffset);
            return ((value & AdvancedColorSupported) != 0,
                (value & AdvancedColorEnabled) != 0,
                (int)BitConverter.ToUInt32(data, AdvancedColorBitsOffset));
        }

        // the mode the panel itself prefers, its native resolution
        private static (int Width, int Height) QueryPreferredMode(byte[] paths, int targetOffset)
        {
            var data = QueryDeviceInfo(paths, targetOffset, PreferredModeType, PreferredModeSize);
            if (data == null) return (0, 0);

            return ((int)BitConverter.ToUInt32(data, PreferredModeWidthOffset), (int)BitConverter.ToUInt32(data, PreferredModeHeightOffset));
        }

        private static int? QuerySdrWhiteLevel(byte[] paths, int targetOffset)
        {
            var data = QueryDeviceInfo(paths, targetOffset, SdrWhiteLevelType, SdrWhiteLevelSize);
            if (data == null) return null;

            return (int)Math.Round(BitConverter.ToUInt32(data, SdrWhiteLevelValueOffset) / 1000.0 * 80);
        }

        private static string ReadString(byte[] data, int offset, int chars)
        {
            var text = MemoryMarshal.Cast<byte, char>(data.AsSpan(offset, chars * 2));
            int end = text.IndexOf('\0');
            return (end < 0 ? text : text[..end]).ToString().Trim();
        }

        // three letters of five bits each, stored big endian in the EDID and handed over as it lies in memory
        private static string DecodeManufacturerId(ushort raw)
        {
            int value = (raw >> 8) | ((raw & 0xFF) << 8);
            var letters = new char[3];
            letters[0] = (char)('A' - 1 + ((value >> 10) & 0x1F));
            letters[1] = (char)('A' - 1 + ((value >> 5) & 0x1F));
            letters[2] = (char)('A' - 1 + (value & 0x1F));

            return letters.All(c => c is >= 'A' and <= 'Z') ? new string(letters) : "";
        }


        // === details ===

        // the scale Windows applies to this display, from its effective dpi; 0 when the monitor is not found
        private static int QueryScalePercent(WinDisplayInfo display)
        {
            IntPtr monitor = FindMonitor(display);
            if (monitor == IntPtr.Zero) return 0;

            return GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint dpiX, out _) == 0
                ? (int)Math.Round(dpiX * 100.0 / 96)
                : 0;
        }

        // the monitor handle of the desktop source the display shows
        private static IntPtr FindMonitor(WinDisplayInfo display)
        {
            if (display.WidthPixels == 0) return IntPtr.Zero;

            return MonitorFromPoint(new POINT { X = display.PositionX, Y = display.PositionY }, MonitorDefaultToNull);
        }

        // peak and black level in nits per GDI device name, as the display reports them; DXGI fills in a default for a
        // display that reports nothing
        private static Dictionary<string, (double Max, double Min)> QueryLuminance()
        {
            var result = new Dictionary<string, (double Max, double Min)>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

                for (uint i = 0; ; i++)
                {
                    if (factory.EnumAdapters1(i, out IDXGIAdapter1 adapter).Failure) break;

                    using (adapter)
                    {
                        for (uint j = 0; ; j++)
                        {
                            if (adapter.EnumOutputs(j, out IDXGIOutput output).Failure) break;

                            using (output)
                            {
                                using var output6 = output.QueryInterfaceOrNull<IDXGIOutput6>();
                                if (output6 == null) continue;

                                var description = output6.Description1;
                                result[description.DeviceName] = (description.MaxLuminance, description.MinLuminance);
                            }
                        }
                    }
                }
            }
            catch
            {
                // no DXGI 1.6 (before Windows 10 1803), or a remote session; the luminance stays unknown
            }

            return result;
        }

        // date, visible size and gamma of every monitor with an EDID; empty when root\wmi has none (a remote session)
        private static List<EdidInfo> QueryEdid()
        {
            var dates = new Dictionary<string, (int? Year, int? Week)>();
            var result = new List<EdidInfo>();

            try
            {
                using (var idSearcher = new ManagementObjectSearcher(
                    @"root\wmi", "SELECT InstanceName, YearOfManufacture, WeekOfManufacture FROM WmiMonitorID"))
                {
                    foreach (ManagementObject item in idSearcher.Get())
                    {
                        string instanceName = item["InstanceName"]?.ToString()?.ToUpperInvariant() ?? "";
                        if (instanceName.Length == 0) continue;

                        // week 0 is "not given", 255 marks a model year
                        int year = Convert.ToInt32(item["YearOfManufacture"] ?? 0);
                        int week = Convert.ToInt32(item["WeekOfManufacture"] ?? 0);
                        dates[instanceName] = (year > 0 ? year : null, week is >= 1 and <= 54 ? week : null);
                    }
                }

                using var sizeSearcher = new ManagementObjectSearcher(
                    @"root\wmi",
                    "SELECT InstanceName, MaxHorizontalImageSize, MaxVerticalImageSize, DisplayTransferCharacteristic FROM WmiMonitorBasicDisplayParams");
                foreach (ManagementObject item in sizeSearcher.Get())
                {
                    string instanceName = item["InstanceName"]?.ToString()?.ToUpperInvariant() ?? "";
                    if (instanceName.Length == 0) continue;

                    // 0 for a projector, whose picture has no fixed size
                    int width = Convert.ToInt32(item["MaxHorizontalImageSize"] ?? 0);
                    int height = Convert.ToInt32(item["MaxVerticalImageSize"] ?? 0);
                    bool hasSize = width > 0 && height > 0;

                    // gamma times 100; 0 and 255 mean it is defined elsewhere
                    int transfer = Convert.ToInt32(item["DisplayTransferCharacteristic"] ?? 0);

                    dates.TryGetValue(instanceName, out var date);
                    dates.Remove(instanceName);

                    result.Add(new EdidInfo(
                        instanceName,
                        date.Year,
                        date.Week,
                        hasSize ? width : null,
                        hasSize ? height : null,
                        transfer is > 0 and < 255 ? transfer / 100.0 : null));
                }
            }
            catch
            {
                // no monitor classes in root\wmi; the facts from the display config stay
            }

            // a monitor that only answered the id class
            foreach (var pair in dates)
            {
                result.Add(new EdidInfo(pair.Key, pair.Value.Year, pair.Value.Week, null, null, null));
            }

            return result;
        }


        // === ddc/ci ===

        // KNOWN UNRELIABLE and untested, see QueryDdcLevels; null when the monitor answers none of the codes
        // kept for DdcFactsMaxAge, so the 10 second refresh of the display list does not put six requests on the bus
        // each time
        private static WinDisplayDdcInfo? QueryDdcFacts(IReadOnlyList<WinDisplayInfo> displays, WinDisplayInfo display)
        {
            lock (_ddcLock)
            {
                if (_ddcFacts.TryGetValue(display.Key, out var cached) && Stopwatch.GetElapsedTime(cached.Timestamp) < DdcFactsMaxAge)
                {
                    return cached.Info;
                }

                WinDisplayDdcInfo? info = null;

                WithPhysicalMonitor(displays, display, handle =>
                {
                    // a non continuous control answers its value in the low byte; version codes use both low bytes
                    uint? powerMode = TryReadVcp(display.Key, handle, VcpPowerMode, out uint value, out _) ? value & 0xFF : null;
                    uint? inputSource = TryReadVcp(display.Key, handle, VcpInputSource, out value, out _) ? value & 0xFF : null;
                    uint? usageHours = TryReadVcp(display.Key, handle, VcpUsageHours, out value, out _) ? value : null;
                    string firmwareLevel = TryReadVcp(display.Key, handle, VcpFirmwareLevel, out value, out _) ? FormatVersion(value) : "";
                    uint? technology = TryReadVcp(display.Key, handle, VcpTechnology, out value, out _) ? value & 0xFF : null;
                    string mccsVersion = TryReadVcp(display.Key, handle, VcpMccsVersion, out value, out _) ? FormatVersion(value) : "";

                    bool answered = powerMode.HasValue || inputSource.HasValue || usageHours.HasValue || technology.HasValue
                        || firmwareLevel.Length > 0 || mccsVersion.Length > 0;
                    if (answered) info = new WinDisplayDdcInfo(powerMode, inputSource, usageHours, firmwareLevel, technology, mccsVersion);
                });

                _ddcFacts[display.Key] = (info, Stopwatch.GetTimestamp());
                return info;
            }
        }

        // "2.1" from the two low bytes
        private static string FormatVersion(uint value) => $"{(value >> 8) & 0xFF}.{value & 0xFF}";

        // opens the physical monitor behind a display, runs the action and closes every handle again; under _ddcLock
        // mirrored displays share one monitor handle with several physical monitors, taken in the order of the displays
        private static void WithPhysicalMonitor(IReadOnlyList<WinDisplayInfo> displays, WinDisplayInfo display, Action<IntPtr> action)
        {
            try
            {
                IntPtr monitor = FindMonitor(display);
                if (monitor == IntPtr.Zero) return;

                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count) || count == 0) return;

                var physicalMonitors = new byte[count * PhysicalMonitorSize];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physicalMonitors)) return;

                try
                {
                    int index = 0;
                    foreach (var other in displays)
                    {
                        if (other.Key == display.Key) break;
                        if (other.GdiDeviceName == display.GdiDeviceName) index++;
                    }

                    if (index < count) action((IntPtr)BitConverter.ToInt64(physicalMonitors, index * PhysicalMonitorSize));
                }
                finally
                {
                    for (int i = 0; i < count; i++)
                    {
                        DestroyPhysicalMonitor((IntPtr)BitConverter.ToInt64(physicalMonitors, i * PhysicalMonitorSize));
                    }
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // no dxva2.dll (a server core install)
            }
        }

        // one VCP code; false when the monitor does not answer it, and without asking while the code is on hold
        private static bool TryReadVcp(string displayKey, IntPtr handle, byte code, out uint current, out uint maximum)
        {
            current = 0;
            maximum = 0;

            var failureKey = (displayKey, code);
            if (_ddcFailures.TryGetValue(failureKey, out var failure) && failure.Failures >= DdcFailureLimit)
            {
                if (Stopwatch.GetElapsedTime(failure.Timestamp) < DdcRetryInterval) return false;
                _ddcFailures.Remove(failureKey);
                failure = default;
            }

            if (GetVCPFeatureAndVCPFeatureReply(handle, code, out _, out current, out maximum))
            {
                _ddcFailures.Remove(failureKey);
                return true;
            }

            _ddcFailures[failureKey] = (failure.Failures + 1, Stopwatch.GetTimestamp());
            return false;
        }


        // === native ===

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdisplayconfigbuffersizes
        [LibraryImport("user32.dll")]
        private static partial int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig
        [LibraryImport("user32.dll")]
        private static partial int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] byte[] pathArray,
            ref uint numModeInfoArrayElements, [Out] byte[] modeInfoArray, IntPtr currentTopologyId);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-displayconfiggetdeviceinfo
        [LibraryImport("user32.dll")]
        private static unsafe partial int DisplayConfigGetDeviceInfo(byte* requestPacket);

        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfrompoint
        [LibraryImport("user32.dll")]
        private static partial IntPtr MonitorFromPoint(POINT point, uint flags);

        // https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor
        [LibraryImport("shcore.dll")]
        private static partial int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        // https://learn.microsoft.com/en-us/windows/win32/api/physicalmonitorenumerationapi/nf-physicalmonitorenumerationapi-getnumberofphysicalmonitorsfromhmonitor
        [LibraryImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint numberOfPhysicalMonitors);

        // https://learn.microsoft.com/en-us/windows/win32/api/physicalmonitorenumerationapi/nf-physicalmonitorenumerationapi-getphysicalmonitorsfromhmonitor
        [LibraryImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint physicalMonitorArraySize, [Out] byte[] physicalMonitorArray);

        // https://learn.microsoft.com/en-us/windows/win32/api/physicalmonitorenumerationapi/nf-physicalmonitorenumerationapi-destroyphysicalmonitor
        [LibraryImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DestroyPhysicalMonitor(IntPtr physicalMonitor);

        // https://learn.microsoft.com/en-us/windows/win32/api/lowlevelmonitorconfigurationapi/nf-lowlevelmonitorconfigurationapi-getvcpfeatureandvcpfeaturereply
        [LibraryImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetVCPFeatureAndVCPFeatureReply(IntPtr physicalMonitor, byte vcpCode, out uint vcpCodeType, out uint currentValue, out uint maximumValue);
    }
}
