using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;


namespace FluentSensors.Core.StaticInfo
{
    // one-time collector for static hardware facts sourced from Windows itself (WMI + native Win32 APIs), as
    // opposed to LhmHardwareTreeService live, continuously-polled sensor data
    // Facts here are queried exactly once, on first access; CPU core count or RAM slot count do not change while
    // the app runs (ideally)
    // lazy singleton, same pattern as PerformanceViewModel/LhmHardwareTreeService
    //
    // NOTE: the constructor runs several WMI queries synchronously and will block whichever thread first
    // touches .Instance for the duration (WMI first query in particular can take a noticeable moment to spin up)
    // Prewarmed on a background thread right at app startup (see MainWindow.StartHardwareServiceAsync), so by
    // the time a page/ViewModel actually needs this data it is normally already sitting ready
    public class WinStaticInfoService
    {
        // Instance may now be created by a background prewarm thread (see MainWindow.StartHardwareServiceAsync) while the
        // UI thread could still request it independently, e.g. if a page opens before the prewarm finishes
        // Lazy<T> with ExecutionAndPublication ensures only one thread runs the constructor and everyone else waits for
        // that same result, instead of two threads racing into duplicate WMI queries
        private static readonly Lazy<WinStaticInfoService> _instance =
            new(() => new WinStaticInfoService(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static WinStaticInfoService Instance => _instance.Value;

        private WinStaticInfoService()
        {
            Cpu = QueryCpu();
            Gpus = QueryGpus();
            Memory = QueryMemory();
            Drives = QueryDrives();
            NetworkAdapters = QueryNetworkAdapters();
            Motherboard = QueryMotherboard();
        }


        // === bindable properties ===
        // (not INotifyPropertyChanged - none of this changes after construction)

        public WinCpuInfo Cpu { get; }
        public IReadOnlyList<WinGpuInfo> Gpus { get; }
        public WinMemoryInfo Memory { get; }
        public IReadOnlyList<WinStorageDriveInfo> Drives { get; }
        public IReadOnlyList<WinNetworkAdapterInfo> NetworkAdapters { get; }
        public WinMotherboardInfo Motherboard { get; }


        // === private query helpers ===

        // cpu
        private static WinCpuInfo QueryCpu()
        {
            var topology = WinCpuTopologyReader.ReadCoreTopology();

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (ManagementObject item in searcher.Get())
            {
                return new WinCpuInfo(
                    PhysicalCores: ToInt(item["NumberOfCores"]),
                    LogicalProcessors: ToInt(item["NumberOfLogicalProcessors"]),
                    L2CacheSizeKb: ToInt(item["L2CacheSize"]),
                    L3CacheSizeKb: ToInt(item["L3CacheSize"]),
                    MaxClockSpeedMhz: ToInt(item["MaxClockSpeed"]),
                    SocketDesignation: item["SocketDesignation"]?.ToString() ?? "",

                    // known unreliable on some systems; see the doc comment on WinCpuInfo for details
                    VirtualizationFirmwareEnabled: ToBool(item["VirtualizationFirmwareEnabled"]),

                    VirtualizationExtensionsSupported: ToBool(item["VMMonitorModeExtensions"]),
                    CoreTopology: topology
                );
            }

            // no Win32_Processor row found (should not normally happen); still return the topology if we have it
            return new WinCpuInfo(0, 0, 0, 0, 0, "", false, false, topology);
        }

        // gpu
        private static List<WinGpuInfo> QueryGpus()
        {
            var result = new List<WinGpuInfo>();

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (ManagementObject item in searcher.Get())
            {
                result.Add(new WinGpuInfo(
                    Name: item["Name"]?.ToString() ?? "",
                    DriverVersion: item["DriverVersion"]?.ToString() ?? "",
                    PnpDeviceId: item["PNPDeviceID"]?.ToString() ?? ""
                ));
            }

            return result;
        }

        // memory
        private static WinMemoryInfo QueryMemory()
        {
            int totalSlots = 0;
            using (var arraySearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemoryArray"))
            {
                foreach (ManagementObject item in arraySearcher.Get())
                {
                    totalSlots += ToInt(item["MemoryDevices"]);
                }
            }

            var modules = new List<WinMemoryModuleInfo>();
            using (var moduleSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory"))
            {
                foreach (ManagementObject item in moduleSearcher.Get())
                {
                    modules.Add(new WinMemoryModuleInfo(
                        Manufacturer: item["Manufacturer"]?.ToString()?.Trim() ?? "",
                        PartNumber: item["PartNumber"]?.ToString()?.Trim() ?? "",
                        CapacityBytes: ToULong(item["Capacity"]),
                        ConfiguredClockSpeedMhz: (uint)ToInt(item["ConfiguredClockSpeed"]),
                        SmbiosMemoryType: (uint)ToInt(item["SMBIOSMemoryType"]),
                        DeviceLocator: item["DeviceLocator"]?.ToString() ?? "",
                        TotalWidthBits: ToInt(item["TotalWidth"]),
                        DataWidthBits: ToInt(item["DataWidth"])
                    ));
                }
            }

            return new WinMemoryInfo(totalSlots, modules);
        }

        // drives
        private static List<WinStorageDriveInfo> QueryDrives()
        {
            var result = new List<WinStorageDriveInfo>();

            // MSFT_PhysicalDisk (modern Storage namespace) is more reliable than the legacy Win32_DiskDrive for two
            // properties:
            // FriendlyName (fixes the blank/garbled NVMe name LHM gave us earlier; confirmed working in the dump) and
            // BusType (Win32_DiskDrive.InterfaceType reports "SCSI" for NVMe drives across the board; a well-known,
            // confirmed Windows quirk, since NVMe is exposed through the storport/SCSI driver model;
            // MSFT_PhysicalDisk.BusType correctly reports "NVMe" instead)
            var physicalDiskInfo = new Dictionary<string, (string FriendlyName, string BusType)>();
            try
            {
                using var storageSearcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk");
                foreach (ManagementObject item in storageSearcher.Get())
                {
                    string serial = item["SerialNumber"]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(serial)) continue;

                    string friendly = item["FriendlyName"]?.ToString()?.Trim() ?? "";
                    string busType = MapBusType(item["BusType"]);
                    physicalDiskInfo[serial] = (friendly, busType);
                }
            }
            catch
            {
                // Storage namespace/MSFT_PhysicalDisk not available on this system:
                // fall back to Win32_DiskDrive names/bus type only, no hard failure
            }

            using var driveSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (ManagementObject item in driveSearcher.Get())
            {
                string serial = item["SerialNumber"]?.ToString()?.Trim() ?? "";
                string legacyModel = item["Model"]?.ToString()?.Trim() ?? "";
                string legacyBusType = item["InterfaceType"]?.ToString() ?? "";

                physicalDiskInfo.TryGetValue(serial, out var modern);

                result.Add(new WinStorageDriveInfo(
                    FriendlyName: !string.IsNullOrEmpty(modern.FriendlyName) ? modern.FriendlyName : legacyModel,
                    SerialNumber: serial,
                    FirmwareRevision: item["FirmwareRevision"]?.ToString()?.Trim() ?? "",
                    BusType: !string.IsNullOrEmpty(modern.BusType) ? modern.BusType : legacyBusType,
                    SizeBytes: ToULong(item["Size"]),
                    PnpDeviceId: item["PNPDeviceID"]?.ToString() ?? ""
                ));
            }

            return result;
        }

        // MSFT_PhysicalDisk.BusType is a numeric enum (STORAGE_BUS_TYPE from the Windows Driver Kit), not a string
        // translated to a readable label here; only the common consumer-hardware values are mapped
        private static string MapBusType(object rawValue)
        {
            if (rawValue == null) return "";
            int value = Convert.ToInt32(rawValue);
            return value switch
            {
                7 => "USB",
                8 => "RAID",
                9 => "iSCSI",
                10 => "SAS",
                11 => "SATA",
                17 => "NVMe",
                _ => $"Unknown ({value})"
            };
        }

        // network
        private static List<WinNetworkAdapterInfo> QueryNetworkAdapters()
        {
            var result = new List<WinNetworkAdapterInfo>();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                // loopback/tunnel pseudo-interfaces pass the Up+HasIP check below (::1/127.0.0.1 count as valid
                // unicast addresses) but are not real hardware
                // excluded explicitly here, since LHMs own hardware discovery apparently never surfaces these as
                // hardware objects in the first place
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var ipProps = nic.GetIPProperties();
                var addresses = ipProps.UnicastAddresses.Select(a => a.Address.ToString()).ToList();
                if (addresses.Count == 0) continue;

                result.Add(new WinNetworkAdapterInfo(
                    Name: nic.Name,
                    Description: nic.Description,
                    MacAddress: nic.GetPhysicalAddress().ToString(),
                    SpeedBitsPerSecond: nic.Speed,
                    InterfaceType: nic.NetworkInterfaceType,
                    IpAddresses: addresses,
                    DhcpEnabled: ipProps.DhcpServerAddresses.Count > 0
                ));
            }

            return result;
        }

        // motherboard
        private static WinMotherboardInfo QueryMotherboard()
        {
            string manufacturer = "", product = "", version = "";
            using (var boardSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard"))
            {
                foreach (ManagementObject item in boardSearcher.Get())
                {
                    manufacturer = item["Manufacturer"]?.ToString()?.Trim() ?? "";
                    product = item["Product"]?.ToString()?.Trim() ?? "";
                    version = item["Version"]?.ToString()?.Trim() ?? "";
                    break; // exactly one baseboard expected
                }
            }

            string biosVersion = "", biosDate = "";
            using (var biosSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS"))
            {
                foreach (ManagementObject item in biosSearcher.Get())
                {
                    biosVersion = item["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? "";
                    biosDate = item["ReleaseDate"]?.ToString() ?? "";
                    break;
                }
            }

            return new WinMotherboardInfo(manufacturer, product, version, biosVersion, biosDate);
        }


        // === WMI value conversion helpers ===
        // WMI returns loosely-typed values (often null, or a numeric type that doesnt exactly match what we expect)
        // these guard against both instead of throwing on a missing/oddly-typed property

        private static int ToInt(object value) => value == null ? 0 : Convert.ToInt32(value);
        private static ulong ToULong(object value) => value == null ? 0 : Convert.ToUInt64(value);
        private static bool ToBool(object value) => value != null && Convert.ToBoolean(value);
    }
}