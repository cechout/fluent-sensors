using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;


namespace FluentSensors.Core.StaticInfo
{
    // the active displays with their mode, connector, HDR state and EDID facts:
    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig
    // the display config part is cheap (well below a millisecond) and is all the sensor source asks for; the EDID part
    // is two WMI queries and only asked for the facts
    // apart from WinDisplayTopology on purpose: that one runs before WinUI starts and answers a narrower question
    public static partial class WinDisplayReader
    {
        // === fields ===

        private const uint QdcOnlyActivePaths = 0x2;
        private const int PathInfoSize = 72; // DISPLAYCONFIG_PATH_INFO
        private const int ModeInfoSize = 64; // DISPLAYCONFIG_MODE_INFO
        private const int PathSourceAdapterOffset = 0; // sourceInfo.adapterId
        private const int PathSourceModeIndexOffset = 12; // sourceInfo.modeInfoIdx
        private const int PathTargetAdapterOffset = 20; // targetInfo.adapterId, then targetInfo.id
        private const int PathTargetTechnologyOffset = 36; // targetInfo.outputTechnology
        private const int PathTargetRefreshRateOffset = 48; // targetInfo.refreshRate
        private const uint ModeIndexInvalid = 0xFFFFFFFF;

        // DISPLAYCONFIG_MODE_INFO: type, id, adapter, then the source mode (width, height)
        private const uint ModeInfoTypeSource = 1;
        private const int ModeSourceWidthOffset = 16;
        private const int ModeSourceHeightOffset = 20;

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

        // DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO: the header, a bit field, the color encoding, the bits per channel
        private const int AdvancedColorType = 9; // DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO
        private const int AdvancedColorSize = 32;
        private const int AdvancedColorValueOffset = 20;
        private const int AdvancedColorBitsOffset = 28;
        private const uint AdvancedColorSupported = 1u << 0;
        private const uint AdvancedColorEnabled = 1u << 1;

        // DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY values of a panel inside the device
        private const uint OutputTechnologyInternal = 0x80000000;
        private const uint OutputTechnologyDisplayPortEmbedded = 11;
        private const uint OutputTechnologyUdiEmbedded = 13;

        private const string BuiltInDisplayName = "Built-in Display";
        private const string UnnamedDisplayName = "Display";

        // the EDID facts of one monitor, by the upper case InstanceName of its root\wmi objects
        private record EdidInfo(string InstanceName, int? ManufactureYear, int? WidthCentimeters, int? HeightCentimeters);


        // === public api ===

        // empty when the display config cannot be read; includeEdid adds the year and the size
        public static List<WinDisplayInfo> QueryDisplays(bool includeEdid)
        {
            var result = new List<WinDisplayInfo>();

            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != 0) return result;

            var paths = new byte[pathCount * PathInfoSize];
            var modes = new byte[modeCount * ModeInfoSize];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return result;

            var edid = includeEdid ? QueryEdid() : new List<EdidInfo>();
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
                int width = 0, height = 0;
                uint modeIndex = BitConverter.ToUInt32(paths, path + PathSourceModeIndexOffset);
                if (modeIndex != ModeIndexInvalid && modeIndex < modeCount)
                {
                    int mode = (int)modeIndex * ModeInfoSize;
                    if (BitConverter.ToUInt32(modes, mode) == ModeInfoTypeSource)
                    {
                        width = (int)BitConverter.ToUInt32(modes, mode + ModeSourceWidthOffset);
                        height = (int)BitConverter.ToUInt32(modes, mode + ModeSourceHeightOffset);
                    }
                }

                var target = QueryTargetName(paths, path + PathTargetAdapterOffset);
                var (isHdrSupported, isHdrEnabled, bitsPerChannel) = QueryAdvancedColor(paths, path + PathTargetAdapterOffset);

                // a second monitor of the same model keeps its own name and key
                string baseName = target.FriendlyName.Length > 0 ? target.FriendlyName : isBuiltIn ? BuiltInDisplayName : UnnamedDisplayName;
                string name = MakeUnique(baseName, usedNames, " #");

                string baseKey = target.EdidKey.Length > 0 ? target.EdidKey : isBuiltIn ? "builtin" : "display";
                string key = MakeUnique(baseKey, usedKeys, "-");

                var monitorEdid = target.InstancePath.Length > 0
                    ? edid.FirstOrDefault(e => e.InstanceName.StartsWith(target.InstancePath, StringComparison.Ordinal))
                    : null;

                result.Add(new WinDisplayInfo(
                    Name: name,
                    Key: key,
                    InstancePath: target.InstancePath,
                    AdapterLuid: ReadLuid(paths, path + PathSourceAdapterOffset),
                    IsBuiltIn: isBuiltIn,
                    OutputTechnology: technology,
                    WidthPixels: width,
                    HeightPixels: height,
                    RefreshRateHz: denominator == 0 ? 0 : Math.Round((double)numerator / denominator, 2),
                    IsHdrSupported: isHdrSupported,
                    IsHdrEnabled: isHdrEnabled,
                    BitsPerColorChannel: bitsPerChannel,
                    ManufacturerId: target.ManufacturerId,
                    ManufactureYear: monitorEdid?.ManufactureYear,
                    WidthCentimeters: monitorEdid?.WidthCentimeters,
                    HeightCentimeters: monitorEdid?.HeightCentimeters));
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


        // === private helpers ===

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

        // the target adapter and id sit in the path in the same order the request header wants them
        private static unsafe byte[]? QueryDeviceInfo(byte[] paths, int targetOffset, int type, int size)
        {
            var request = new byte[size];
            BitConverter.TryWriteBytes(request.AsSpan(0), type);
            BitConverter.TryWriteBytes(request.AsSpan(4), size);
            Array.Copy(paths, targetOffset, request, 8, 12);

            fixed (byte* buffer = request)
            {
                return DisplayConfigGetDeviceInfo(buffer) == 0 ? request : null;
            }
        }

        private static (string FriendlyName, string ManufacturerId, string EdidKey, string InstancePath) QueryTargetName(byte[] paths, int targetOffset)
        {
            var data = QueryDeviceInfo(paths, targetOffset, TargetNameType, TargetNameSize);
            if (data == null) return ("", "", "", "");

            string friendlyName = ReadString(data, TargetNameFriendlyNameOffset, TargetNameFriendlyNameChars);
            string devicePath = ReadString(data, TargetNameDevicePathOffset, TargetNameDevicePathChars);

            string manufacturerId = "";
            string edidKey = "";
            if ((BitConverter.ToUInt32(data, TargetNameFlagsOffset) & TargetNameEdidIdsValid) != 0)
            {
                manufacturerId = DecodeManufacturerId(BitConverter.ToUInt16(data, TargetNameManufactureIdOffset));
                ushort productCode = BitConverter.ToUInt16(data, TargetNameProductCodeOffset);
                if (manufacturerId.Length > 0) edidKey = $"{manufacturerId}{productCode:X4}".ToLowerInvariant();
            }

            return (friendlyName, manufacturerId, edidKey, ToInstancePath(devicePath));
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

        // year and visible size of every monitor with an EDID; empty when root\wmi has none (a remote session)
        private static List<EdidInfo> QueryEdid()
        {
            var years = new Dictionary<string, int>();
            var result = new List<EdidInfo>();

            try
            {
                using (var idSearcher = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, YearOfManufacture FROM WmiMonitorID"))
                {
                    foreach (ManagementObject item in idSearcher.Get())
                    {
                        string instanceName = item["InstanceName"]?.ToString()?.ToUpperInvariant() ?? "";
                        int year = Convert.ToInt32(item["YearOfManufacture"] ?? 0);
                        if (instanceName.Length > 0 && year > 0) years[instanceName] = year;
                    }
                }

                using var sizeSearcher = new ManagementObjectSearcher(
                    @"root\wmi", "SELECT InstanceName, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams");
                foreach (ManagementObject item in sizeSearcher.Get())
                {
                    string instanceName = item["InstanceName"]?.ToString()?.ToUpperInvariant() ?? "";
                    if (instanceName.Length == 0) continue;

                    // 0 for a projector, whose picture has no fixed size
                    int width = Convert.ToInt32(item["MaxHorizontalImageSize"] ?? 0);
                    int height = Convert.ToInt32(item["MaxVerticalImageSize"] ?? 0);
                    bool hasSize = width > 0 && height > 0;

                    result.Add(new EdidInfo(
                        instanceName,
                        years.TryGetValue(instanceName, out int year) ? year : null,
                        hasSize ? width : null,
                        hasSize ? height : null));
                    years.Remove(instanceName);
                }
            }
            catch
            {
                // no monitor classes in root\wmi; the facts from the display config stay
            }

            // a monitor that only answered the id class
            foreach (var pair in years)
            {
                result.Add(new EdidInfo(pair.Key, pair.Value, null, null));
            }

            return result;
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
    }
}
