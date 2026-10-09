using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;
using Vortice.DXGI;
using System.Net.Sockets;

using FluentSensors.Common.Localization;


namespace FluentSensors.Core.StaticInfo
{
    // static hardware info:
    // facts from Windows itself (WMI and Win32), queried once on first access, unlike the live
    // LHM sensors; a lazy singleton
    // the constructor blocks the first thread that touches Instance through several WMI queries, so it is prewarmed at
    // startup (see MainWindow.StartHardwareServiceAsync)
    public class WinStaticInfoService
    {
        // the prewarm thread and a page on the UI thread can both ask; ExecutionAndPublication runs the constructor
        // once and lets the other wait
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
            IsDotNetRuntimeInstalled = QueryDotNetRuntimeInstalled();
            IsPawnIoInstalled = QueryPawnIoInstalled();
        }


        // === public binding surface ===
        // (no INotifyPropertyChanged, nothing changes after construction)

        public WinCpuInfo Cpu { get; }
        public IReadOnlyList<WinGpuInfo> Gpus { get; }
        public WinMemoryInfo Memory { get; }
        public IReadOnlyList<WinStorageDriveInfo> Drives { get; }
        public IReadOnlyList<WinNetworkAdapterInfo> NetworkAdapters { get; }
        public WinMotherboardInfo Motherboard { get; }

        // true once any Major >= 10 shared framework version is found, see QueryDotNetRuntimeInstalled
        public bool IsDotNetRuntimeInstalled { get; }

        // true while the PawnIO kernel driver is present, see QueryPawnIoInstalled
        public bool IsPawnIoInstalled { get; }


        // === private helpers ===

        // cpu
        private static WinCpuInfo QueryCpu()
        {
            var topology = WinCpuTopologyReader.ReadCoreTopology();

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (ManagementObject item in searcher.Get())
            {
                // one entry per cache instance; Level is the raw WMI value, see
                // WinCpuCacheEntry for the L1/L2/L3 mapping
                // GetRelated() walks the association like an "ASSOCIATORS OF" query, without
                // naming the association class
                var cacheEntries = new List<WinCpuCacheEntry>();
                try
                {
                    foreach (ManagementObject cache in item.GetRelated("Win32_CacheMemory"))
                    {
                        cacheEntries.Add(new WinCpuCacheEntry(
                            Level: (uint)ToInt(cache["Level"]),
                            CacheTypeText: HardwareInfoFormatter.FormatCacheType((uint)ToInt(cache["CacheType"])),
                            SizeKb: (uint)ToInt(cache["MaxCacheSize"])
                        ));
                    }
                }
                catch
                {
                    // no Win32_CacheMemory association here; cacheEntries stays empty
                }

                return new WinCpuInfo(
                    PhysicalCores: ToInt(item["NumberOfCores"]),
                    LogicalProcessors: ToInt(item["NumberOfLogicalProcessors"]),
                    MaxClockSpeedMhz: ToInt(item["MaxClockSpeed"]),
                    SocketDesignation: item["SocketDesignation"]?.ToString() ?? "",

                    // known unreliable, see WinCpuInfo for the confirmed case
                    VirtualizationFirmwareEnabled: ToBool(item["VirtualizationFirmwareEnabled"]),

                    VirtualizationExtensionsSupported: ToBool(item["VMMonitorModeExtensions"]),
                    CoreTopology: topology,
                    CacheEntries: cacheEntries
                );
            }

            // no Win32_Processor row; the topology still goes back
            return new WinCpuInfo(0, 0, 0, "", false, false, topology, new List<WinCpuCacheEntry>());
        }


        // gpu
        private static List<WinGpuInfo> QueryGpus()
        {
            var dxgiAdapters = QueryDxgiAdapters();
            var result = new List<WinGpuInfo>();

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (ManagementObject item in searcher.Get())
            {
                string name = item["Name"]?.ToString() ?? "";

                // best-effort match against DXGI; its Description is near-identical to the WMI Name but not
                // byte-identical, so HardwareNameMatcher
                var dxgiMatch = HardwareNameMatcher.FindBestMatch(name, dxgiAdapters, a => a.Description);

                result.Add(new WinGpuInfo(
                    Name: name,
                    DriverVersion: item["DriverVersion"]?.ToString() ?? "",
                    PnpDeviceId: item["PNPDeviceID"]?.ToString() ?? "",
                    VendorId: dxgiMatch?.VendorId ?? 0,
                    DeviceId: dxgiMatch?.DeviceId ?? 0,
                    DedicatedVideoMemoryBytes: dxgiMatch?.DedicatedVideoMemory ?? 0,
                    DedicatedSystemMemoryBytes: dxgiMatch?.DedicatedSystemMemory ?? 0,
                    SharedSystemMemoryBytes: dxgiMatch?.SharedSystemMemory ?? 0,
                    AdapterLuid: dxgiMatch?.Luid ?? 0,
                    Displays: Array.Empty<WinDisplay>(),
                    DisplayViaGpuName: null
                ));
            }

            return WithDisplays(result);
        }

        // each gpu with the displays it scans out; a hybrid discrete one without any renders through the integrated one
        // that drives the panel
        private static List<WinGpuInfo> WithDisplays(List<WinGpuInfo> gpus)
        {
            List<WinDisplay>? displays = null;
            try { displays = WinDisplayTopology.QueryDisplays(); }
            catch { /* no display config; the gpus keep no display line */ }
            if (displays == null) return gpus;

            var withDisplays = gpus
                .Select(g => g with { Displays = g.AdapterLuid == 0 ? new List<WinDisplay>() : displays.FindAll(d => d.AdapterLuid == g.AdapterLuid) })
                .ToList();

            string? integratedName = withDisplays
                .FirstOrDefault(g => g.Displays.Count > 0 && WinDisplayTopology.HybridRole(g.AdapterLuid) == WinHybridRole.Integrated)?
                .Name;

            return withDisplays
                .Select(g => g.Displays.Count == 0 && g.AdapterLuid != 0 && WinDisplayTopology.HybridRole(g.AdapterLuid) == WinHybridRole.Discrete
                    ? g with { DisplayViaGpuName = integratedName }
                    : g)
                .ToList();
        }

        // DXGI-only facts, matched to the WMI GPUs by name; an intermediate step only
        private record DxgiAdapterInfo(
            string Description,
            long Luid,
            uint VendorId,
            uint DeviceId,
            ulong DedicatedVideoMemory,
            ulong DedicatedSystemMemory,
            ulong SharedSystemMemory);

        // DXGI is the source of truth for video memory: Win32_VideoController.AdapterRAM is 32-bit and wraps above
        // 4 GB, DXGI has no cap and works the same on every vendor
        private static List<DxgiAdapterInfo> QueryDxgiAdapters()
        {
            var result = new List<DxgiAdapterInfo>();

            try
            {
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

                for (uint i = 0; ; i++)
                {
                    var hr = factory.EnumAdapters1(i, out IDXGIAdapter1 adapter);
                    if (hr.Failure) break; // no more adapters

                    using (adapter)
                    {
                        var desc = adapter.Description1;
                        result.Add(new DxgiAdapterInfo(
                            desc.Description,
                            desc.Luid,
                            (uint)desc.VendorId,
                            (uint)desc.DeviceId,
                            (ulong)desc.DedicatedVideoMemory,
                            (ulong)desc.DedicatedSystemMemory,
                            (ulong)desc.SharedSystemMemory
                        ));
                    }
                }
            }
            catch
            {
                // no DXGI (very old system, odd remote session); the WMI fields still work
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
                        SerialNumber: item["SerialNumber"]?.ToString()?.Trim() ?? "",
                        CapacityBytes: ToULong(item["Capacity"]),
                        ConfiguredClockSpeedMhz: (uint)ToInt(item["ConfiguredClockSpeed"]),
                        RatedSpeedMhz: (uint)ToInt(item["Speed"]),
                        SmbiosMemoryType: (uint)ToInt(item["SMBIOSMemoryType"]),
                        DeviceLocator: item["DeviceLocator"]?.ToString() ?? "",
                        BankLabel: item["BankLabel"]?.ToString() ?? "",
                        FormFactor: (uint)ToInt(item["FormFactor"]),
                        Rank: (uint)ToInt(item["Attributes"]),
                        ConfiguredVoltageMillivolts: (uint)ToInt(item["ConfiguredVoltage"]),
                        MinVoltageMillivolts: (uint)ToInt(item["MinVoltage"]),
                        MaxVoltageMillivolts: (uint)ToInt(item["MaxVoltage"]),
                        TotalWidthBits: ToInt(item["TotalWidth"]),
                        DataWidthBits: ToInt(item["DataWidth"])
                    ));
                }
            }

            return new WinMemoryInfo(totalSlots, modules);
        }

        // MSFT_PhysicalDisk and its MSFT_StorageReliabilityCounter, keyed by serial and matched to Win32_DiskDrive
        // below; an intermediate step like DxgiAdapterInfo
        private record PhysicalDiskExtraInfo(
            string FriendlyName,
            string BusType,
            uint? TemperatureCelsius,
            uint? TemperatureMaxCelsius,
            uint? WearPercent,
            uint? PowerOnHours,
            ulong? ReadErrorsTotal,
            ulong? ReadErrorsCorrected,
            ulong? ReadErrorsUncorrected,
            ulong? WriteErrorsTotal,
            ulong? WriteErrorsCorrected,
            ulong? WriteErrorsUncorrected,
            uint? StartStopCycleCount,
            uint? StartStopCycleCountMax,
            uint? LoadUnloadCycleCount,
            uint? LoadUnloadCycleCountMax,
            string ManufactureDate,
            ulong? ReadLatencyMaxMs,
            ulong? WriteLatencyMaxMs,
            ulong? FlushLatencyMaxMs);


        // drives
        private static List<WinStorageDriveInfo> QueryDrives()
        {
            var result = new List<WinStorageDriveInfo>();

            // --- workaround: Win32_DiskDrive unreliable for NVMe bus type/naming ---
            // problem: Win32_DiskDrive.InterfaceType reports "SCSI" for every NVMe drive (storport classifies through
            // its SCSI-descended scheme), and Model is sometimes blank or garbled; Microsoft support confirms and
            // points to MSFT_PhysicalDisk.BusType:
            // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/storport-driver-overview
            // https://social.msdn.microsoft.com/Forums/en-US/3cb7d1ab-e9f0-4ddb-87d4-cfee3d3915c5/the-interfacetype-of-win32diskdrive-reports-scsi-instead-of-nvme
            // fix: MSFT_PhysicalDisk from the Storage namespace (FriendlyName, BusType "NVMe"), Win32_DiskDrive only
            // where it has nothing for a serial:
            // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisk
            var physicalDiskInfo = new Dictionary<string, PhysicalDiskExtraInfo>();
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

                    // SMART-style reliability counters, the Storport view of the drive health data; GetRelated() walks
                    // MSFT_PhysicalDiskToStorageReliabilityCounter without naming it
                    // a field the driver never reports stays null, not 0 (checked field by field against
                    // Get-StorageReliabilityCounter)
                    uint? temperature = null, temperatureMax = null, wear = null, powerOnHours = null;
                    ulong? readErrorsTotal = null, readErrorsCorrected = null, readErrorsUncorrected = null;
                    ulong? writeErrorsTotal = null, writeErrorsCorrected = null, writeErrorsUncorrected = null;
                    uint? startStopCycleCount = null, startStopCycleCountMax = null;
                    uint? loadUnloadCycleCount = null, loadUnloadCycleCountMax = null;
                    string manufactureDate = "";
                    ulong? readLatencyMax = null, writeLatencyMax = null, flushLatencyMax = null;

                    try
                    {
                        foreach (ManagementObject reliability in item.GetRelated("MSFT_StorageReliabilityCounter"))
                        {
                            temperature = ToNullableUInt(reliability["Temperature"]);
                            temperatureMax = ToNullableUInt(reliability["TemperatureMax"]);
                            wear = ToNullableUInt(reliability["Wear"]);
                            powerOnHours = ToNullableUInt(reliability["PowerOnHours"]);
                            readErrorsTotal = ToNullableULong(reliability["ReadErrorsTotal"]);
                            readErrorsCorrected = ToNullableULong(reliability["ReadErrorsCorrected"]);
                            readErrorsUncorrected = ToNullableULong(reliability["ReadErrorsUncorrected"]);
                            writeErrorsTotal = ToNullableULong(reliability["WriteErrorsTotal"]);
                            writeErrorsCorrected = ToNullableULong(reliability["WriteErrorsCorrected"]);
                            writeErrorsUncorrected = ToNullableULong(reliability["WriteErrorsUncorrected"]);
                            startStopCycleCount = ToNullableUInt(reliability["StartStopCycleCount"]);
                            startStopCycleCountMax = ToNullableUInt(reliability["StartStopCycleCountMax"]);
                            loadUnloadCycleCount = ToNullableUInt(reliability["LoadUnloadCycleCount"]);
                            loadUnloadCycleCountMax = ToNullableUInt(reliability["LoadUnloadCycleCountMax"]);
                            manufactureDate = reliability["ManufactureDate"]?.ToString() ?? "";
                            readLatencyMax = ToNullableULong(reliability["ReadLatencyMax"]);
                            writeLatencyMax = ToNullableULong(reliability["WriteLatencyMax"]);
                            flushLatencyMax = ToNullableULong(reliability["FlushLatencyMax"]);
                            break; // one per physical disk
                        }
                    }
                    catch
                    {
                        // no reliability counter for this disk (some drivers expose none); everything stays null
                    }

                    physicalDiskInfo[serial] = new PhysicalDiskExtraInfo(
                        friendly, busType, temperature, temperatureMax, wear, powerOnHours,
                        readErrorsTotal, readErrorsCorrected, readErrorsUncorrected,
                        writeErrorsTotal, writeErrorsCorrected, writeErrorsUncorrected,
                        startStopCycleCount, startStopCycleCountMax,
                        loadUnloadCycleCount, loadUnloadCycleCountMax,
                        manufactureDate, readLatencyMax, writeLatencyMax, flushLatencyMax);
                }
            }
            catch
            {
                // no Storage namespace; Win32_DiskDrive names and bus type only
            }

            using var driveSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (ManagementObject item in driveSearcher.Get())
            {
                string serial = item["SerialNumber"]?.ToString()?.Trim() ?? "";
                string legacyModel = item["Model"]?.ToString()?.Trim() ?? "";
                string legacyBusType = item["InterfaceType"]?.ToString() ?? "";

                physicalDiskInfo.TryGetValue(serial, out var modern);

                result.Add(new WinStorageDriveInfo(
                    FriendlyName: !string.IsNullOrEmpty(modern?.FriendlyName) ? modern.FriendlyName : legacyModel,
                    SerialNumber: serial,
                    FirmwareRevision: item["FirmwareRevision"]?.ToString()?.Trim() ?? "",
                    BusType: !string.IsNullOrEmpty(modern?.BusType) ? modern.BusType : legacyBusType,
                    SizeBytes: ToULong(item["Size"]),
                    PnpDeviceId: item["PNPDeviceID"]?.ToString() ?? "",
                    TemperatureCelsius: modern?.TemperatureCelsius,
                    TemperatureMaxCelsius: modern?.TemperatureMaxCelsius,
                    WearPercent: modern?.WearPercent,
                    PowerOnHours: modern?.PowerOnHours,
                    ReadErrorsTotal: modern?.ReadErrorsTotal,
                    ReadErrorsCorrected: modern?.ReadErrorsCorrected,
                    ReadErrorsUncorrected: modern?.ReadErrorsUncorrected,
                    WriteErrorsTotal: modern?.WriteErrorsTotal,
                    WriteErrorsCorrected: modern?.WriteErrorsCorrected,
                    WriteErrorsUncorrected: modern?.WriteErrorsUncorrected,
                    StartStopCycleCount: modern?.StartStopCycleCount,
                    StartStopCycleCountMax: modern?.StartStopCycleCountMax,
                    LoadUnloadCycleCount: modern?.LoadUnloadCycleCount,
                    LoadUnloadCycleCountMax: modern?.LoadUnloadCycleCountMax,
                    ManufactureDate: modern?.ManufactureDate ?? "",
                    ReadLatencyMaxMs: modern?.ReadLatencyMaxMs,
                    WriteLatencyMaxMs: modern?.WriteLatencyMaxMs,
                    FlushLatencyMaxMs: modern?.FlushLatencyMaxMs
                ));
            }

            return result;
        }

        // STORAGE_BUS_TYPE (Windows Driver Kit) to a label; the common consumer values only
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
                _ => AppStrings.Format("Info_UnknownValue", value)
            };
        }


        // network
        private static List<WinNetworkAdapterInfo> QueryNetworkAdapters()
        {
            var result = new List<WinNetworkAdapterInfo>();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                // loopback and tunnel pass the up and address check (::1 and 127.0.0.1 count) but are no hardware;
                // (LHM never lists them either)
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var ipProps = nic.GetIPProperties();
                var unicastAddresses = ipProps.UnicastAddresses.Select(a => a.Address).ToList();
                if (unicastAddresses.Count == 0) continue;

                // split by address family; (IPv4 for the LAN, IPv6 for external reachability, a merged list
                // makes the reader sort them)
                var ipv4Addresses = unicastAddresses
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.ToString())
                    .ToList();
                var ipv6Addresses = unicastAddresses
                    .Where(a => a.AddressFamily == AddressFamily.InterNetworkV6)
                    .Select(a => a.ToString())
                    .ToList();

                result.Add(new WinNetworkAdapterInfo(
                    Name: nic.Name,
                    Description: nic.Description,
                    MacAddress: nic.GetPhysicalAddress().ToString(),
                    SpeedBitsPerSecond: nic.Speed,
                    InterfaceType: nic.NetworkInterfaceType,
                    IPv4Addresses: ipv4Addresses,
                    IPv6Addresses: ipv6Addresses,
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
                    break; // one baseboard
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

        // every reading that needs ring 0 goes through PawnIO (cpu temperature and power, motherboard and SuperIO
        // sensors, memory timings); without the driver those are absent, the rest keeps working
        // asks LibreHardwareMonitorLib, the library that has to use it; the property is static and needs neither
        // Computer.Open nor elevation
        private static bool QueryPawnIoInstalled()
        {
            try
            {
                return LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
            }
            catch
            {
                // a library version without the property counts as installed, so the hint never shows on a fine machine
                return true;
            }
        }

        // many LHM sensors (several CPU and GPU ones) only populate with a .NET Desktop Runtime 10 or newer installed
        // system-wide, whatever the bundled runtime
        // asks dotnet --list-runtimes, which reads the shared framework folders; the Setup/InstalledVersions registry
        // tree misses anything the Visual Studio Installer put there since VS2019 16.3
        private static bool QueryDotNetRuntimeInstalled()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "--list-runtimes",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(2000); // never block the launch on a hung process

                return output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Any(HasMajorVersion10OrNewer);
            }
            catch
            {
                return false; // no dotnet on PATH, or it failed to start
            }
        }

        // "Microsoft.WindowsDesktop.App 10.0.0 [C:\Program Files\dotnet\shared\...]"; the version is the second token
        private static bool HasMajorVersion10OrNewer(string listRuntimesLine)
        {
            var parts = listRuntimesLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && Version.TryParse(parts[1], out var version) && version.Major >= 10;
        }


        // WMI conversion; values come loosely typed (often null, or another numeric type), these do not throw on either
        private static int ToInt(object value) => value == null ? 0 : Convert.ToInt32(value);
        private static ulong ToULong(object value) => value == null ? 0 : Convert.ToUInt64(value);
        private static bool ToBool(object value) => value != null && Convert.ToBoolean(value);
        private static uint? ToNullableUInt(object value) => value == null ? (uint?)null : Convert.ToUInt32(value);
        private static ulong? ToNullableULong(object value) => value == null ? (ulong?)null : Convert.ToUInt64(value);
    }
}
