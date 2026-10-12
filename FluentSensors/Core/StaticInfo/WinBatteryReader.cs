using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;


namespace FluentSensors.Core.StaticInfo
{
    // one battery as its firmware describes it
    public record WinBatteryInfo(
        string Name, // "ASUS Battery", the name LHM lists the battery under
        string Manufacturer,
        string Chemistry, // the raw four characters, "LIon"; see HardwareInfoFormatter.FormatBatteryChemistry
        uint? DesignedCapacityMilliwattHours, // null when unknown or reported without a unit
        uint? FullChargedCapacityMilliwattHours,
        uint? CycleCount, // null when the firmware does not count
        string ManufactureDate // "" when not reported
    );

    // the battery facts:
    // asked from the battery driver itself (IOCTL_BATTERY_QUERY_INFORMATION), the way LHM reads its sensors; LHM keeps
    // manufacturer, chemistry and cycle count internal, and the WMI class for them (root\wmi BatteryStaticData) is
    // empty on current Windows
    // KNOWN UNRELIABLE: Win32_Battery.Chemistry, which answers "lead acid" for a lithium ion pack; not used
    // https://learn.microsoft.com/en-us/windows/win32/power/ioctl-battery-query-information
    public static partial class WinBatteryReader
    {
        // === fields ===

        private static Guid BatteryInterfaceId = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a"); // GUID_DEVICE_BATTERY

        private const uint IoctlBatteryQueryTag = 0x294040;
        private const uint IoctlBatteryQueryInformation = 0x294044;

        // BATTERY_QUERY_INFORMATION_LEVEL
        private const int LevelInformation = 0;
        private const int LevelDeviceName = 4;
        private const int LevelManufactureDate = 5;
        private const int LevelManufactureName = 6;

        // BATTERY_INFORMATION: capabilities, technology (1 byte + 3 reserved), chemistry (4 chars), then six ULONG
        private const int InformationSize = 36;
        private const int InformationChemistryOffset = 8;
        private const int InformationDesignedCapacityOffset = 12;
        private const int InformationFullChargedCapacityOffset = 16;
        private const int InformationCycleCountOffset = 32;

        private const uint CapabilityCapacityRelative = 0x40000000; // capacities without a unit, not mWh
        private const uint CapacityUnknown = 0xFFFFFFFF;

        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareReadWrite = 0x3;
        private const uint OpenExisting = 3;
        private static readonly IntPtr InvalidHandle = new(-1);


        // === public api ===

        // every battery that is present; empty on a desktop or when the driver does not answer
        public static List<WinBatteryInfo> QueryBatteries()
        {
            var result = new List<WinBatteryInfo>();

            foreach (string path in QueryInterfacePaths())
            {
                IntPtr handle = CreateFileW(path, GenericRead | GenericWrite, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (handle == InvalidHandle) continue;

                try
                {
                    var battery = ReadBattery(handle);
                    if (battery != null) result.Add(battery);
                }
                finally
                {
                    CloseHandle(handle);
                }
            }

            return result;
        }


        // === private helpers ===

        private static WinBatteryInfo? ReadBattery(IntPtr handle)
        {
            // the tag names the pack that sits in the slot right now; 0 means the slot is empty
            var wait = new byte[4];
            var tagBuffer = new byte[4];
            if (!DeviceIoControl(handle, IoctlBatteryQueryTag, wait, 4, tagBuffer, 4, out _, IntPtr.Zero)) return null;

            uint tag = BitConverter.ToUInt32(tagBuffer, 0);
            if (tag == 0) return null;

            var information = Query(handle, tag, LevelInformation, InformationSize);
            if (information == null || information.Length < InformationSize) return null;

            bool isRelative = (BitConverter.ToUInt32(information, 0) & CapabilityCapacityRelative) != 0;
            uint cycleCount = BitConverter.ToUInt32(information, InformationCycleCountOffset);

            return new WinBatteryInfo(
                Name: QueryText(handle, tag, LevelDeviceName),
                Manufacturer: QueryText(handle, tag, LevelManufactureName),
                Chemistry: Encoding.ASCII.GetString(information, InformationChemistryOffset, 4).TrimEnd('\0', ' '),
                DesignedCapacityMilliwattHours: ToCapacity(information, InformationDesignedCapacityOffset, isRelative),
                FullChargedCapacityMilliwattHours: ToCapacity(information, InformationFullChargedCapacityOffset, isRelative),
                CycleCount: cycleCount > 0 ? cycleCount : null,
                ManufactureDate: QueryManufactureDate(handle, tag));
        }

        private static uint? ToCapacity(byte[] information, int offset, bool isRelative)
        {
            uint value = BitConverter.ToUInt32(information, offset);
            return isRelative || value == 0 || value == CapacityUnknown ? null : value;
        }

        // BATTERY_MANUFACTURE_DATE: day, month, then the year as two bytes
        private static string QueryManufactureDate(IntPtr handle, uint tag)
        {
            var date = Query(handle, tag, LevelManufactureDate, 4);
            if (date == null || date.Length < 4) return "";

            int year = BitConverter.ToUInt16(date, 2);
            if (year == 0 || date[1] is 0 or > 12 || date[0] is 0 or > 31) return "";

            return $"{year:0000}-{date[1]:00}-{date[0]:00}";
        }

        private static string QueryText(IntPtr handle, uint tag, int level)
        {
            var text = Query(handle, tag, level, 512);
            return text == null ? "" : Encoding.Unicode.GetString(text).TrimEnd('\0').Trim();
        }

        // the answer cut to what the driver wrote; null when it does not know the level
        private static byte[]? Query(IntPtr handle, uint tag, int level, int size)
        {
            // BATTERY_QUERY_INFORMATION: tag, level, at rate
            var query = new byte[12];
            BitConverter.TryWriteBytes(query.AsSpan(0), tag);
            BitConverter.TryWriteBytes(query.AsSpan(4), level);

            var answer = new byte[size];
            if (!DeviceIoControl(handle, IoctlBatteryQueryInformation, query, (uint)query.Length, answer, (uint)size, out uint written, IntPtr.Zero)) return null;

            return answer.AsSpan(0, (int)Math.Min(written, (uint)size)).ToArray();
        }

        // the device interface paths of the battery class, a list of strings behind each other
        private static List<string> QueryInterfacePaths()
        {
            var result = new List<string>();

            try
            {
                if (CM_Get_Device_Interface_List_SizeW(out uint length, ref BatteryInterfaceId, IntPtr.Zero, 0) != 0 || length <= 1) return result;

                // the length counts characters, two bytes each
                var buffer = new byte[length * 2];
                if (CM_Get_Device_Interface_ListW(ref BatteryInterfaceId, IntPtr.Zero, buffer, length, 0) != 0) return result;

                foreach (string path in Encoding.Unicode.GetString(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
                {
                    result.Add(path);
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // no cfgmgr32; no battery facts
            }

            return result;
        }


        // === win32 api imports ===

        [LibraryImport("cfgmgr32.dll")]
        private static partial int CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid interfaceClassId, IntPtr deviceId, uint flags);

        [LibraryImport("cfgmgr32.dll")]
        private static partial int CM_Get_Device_Interface_ListW(ref Guid interfaceClassId, IntPtr deviceId, [Out] byte[] buffer, uint bufferLength, uint flags);

        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        private static partial IntPtr CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeviceIoControl(IntPtr device, uint controlCode, byte[] inBuffer, uint inBufferSize, [Out] byte[] outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(IntPtr handle);
    }
}
