using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;
using Vortice.DXGI;
using System.Net.Sockets;


namespace FluentSensors.Core.StaticInfo
{
    // one-time collector for static hardware facts sourced from Windows itself (WMI + native Win32 APIs), as
    // opposed to LhmHardwareTreeService live, continuously-polled sensor data
    // facts here are queried exactly once, on first access; CPU core count or RAM slot count do not change while
    // the app runs (ideally)
    // lazy singleton, same pattern as PerformanceViewModel/LhmHardwareTreeService
    //
    // the constructor runs several WMI queries synchronously and blocks whichever thread first touches .Instance
    // for the duration (the first WMI query in particular can take a noticeable moment to spin up); prewarmed on
    // a background thread right at app startup (see MainWindow.StartHardwareServiceAsync), so by the time a
    // page/ViewModel actually needs this data it is normally already sitting ready
    public class WinStaticInfoService
    {
        // instance may now be created by a background prewarm thread (see MainWindow.StartHardwareServiceAsync)
        // while the UI thread could still request it independently, e.g. if a page opens before the prewarm
        // finishes
        // Lazy<T> with ExecutionAndPublication ensures only one thread runs the constructor and everyone else
        // waits for that same result, instead of two threads racing into duplicate WMI queries
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
        }


        // === Public Binding Surface ===
        // (not INotifyPropertyChanged; none of this changes after construction)

        public WinCpuInfo Cpu { get; }
        public IReadOnlyList<WinGpuInfo> Gpus { get; }
        public WinMemoryInfo Memory { get; }
        public IReadOnlyList<WinStorageDriveInfo> Drives { get; }
        public IReadOnlyList<WinNetworkAdapterInfo> NetworkAdapters { get; }
        public WinMotherboardInfo Motherboard { get; }

        // true once any Major >= 10 shared framework version is found, see QueryDotNetRuntimeInstalled
        public bool IsDotNetRuntimeInstalled { get; }


        // === Private Helpers ===

        // cpu
        private static WinCpuInfo QueryCpu()
        {
            var topology = WinCpuTopologyReader.ReadCoreTopology();

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (ManagementObject item in searcher.Get())
            {
                // raw per-cache-instance facts; Level is the raw WMI value, see WinCpuCacheEntry for the
                // confirmed L1/L2/L3 mapping
                // GetRelated() is the same built-in association traversal already used for MSFT_StorageReliabilityCounter;
                // equivalent to an "ASSOCIATORS OF" query without needing to know/construct the association class by hand
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
                    // Win32_CacheMemory not populated/associated on this system; cacheEntries stays empty
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

            // no Win32_Processor row found (should not normally happen); still return the topology if we have it
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

                // best-effort match against the DXGI-enumerated adapters; DXGIs own Description string is
                // usually near-identical to WMIs Name but not guaranteed byte-identical, so this goes through
                // the same name-matching approach already used elsewhere for multi-device categories
                // (HardwareNameMatcher) instead of an exact string comparison
                var dxgiMatch = HardwareNameMatcher.FindBestMatch(name, dxgiAdapters, a => a.Description);

                result.Add(new WinGpuInfo(
                    Name: name,
                    DriverVersion: item["DriverVersion"]?.ToString() ?? "",
                    PnpDeviceId: item["PNPDeviceID"]?.ToString() ?? "",
                    VendorId: dxgiMatch?.VendorId ?? 0,
                    DeviceId: dxgiMatch?.DeviceId ?? 0,
                    DedicatedVideoMemoryBytes: dxgiMatch?.DedicatedVideoMemory ?? 0,
                    DedicatedSystemMemoryBytes: dxgiMatch?.DedicatedSystemMemory ?? 0,
                    SharedSystemMemoryBytes: dxgiMatch?.SharedSystemMemory ?? 0
                ));
            }

            return result;
        }

        // small local carrier for DXGI-only facts, matched against the WMI-discovered GPUs above by name; not
        // part of the public WinGpuInfo model, purely an intermediate step
        private record DxgiAdapterInfo(
            string Description,
            uint VendorId,
            uint DeviceId,
            ulong DedicatedVideoMemory,
            ulong DedicatedSystemMemory,
            ulong SharedSystemMemory);

        // DXGI is the source of truth for dedicated/shared video memory:
        // Win32_VideoController.AdapterRAM is a 32-bit WMI field, well documented to report wrong (wrapped-around)
        // values on any GPU with more than 4GB VRAM;
        // DXGI has no such cap and works identically across NVIDIA/AMD/Intel since it's a DirectX-level abstraction,
        // not a vendor-specific driver API
        private static List<DxgiAdapterInfo> QueryDxgiAdapters()
        {
            var result = new List<DxgiAdapterInfo>();

            try
            {
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

                for (uint i = 0; ; i++)
                {
                    var hr = factory.EnumAdapters1(i, out IDXGIAdapter1 adapter);
                    if (hr.Failure) break; // no more adapters at this index

                    using (adapter)
                    {
                        var desc = adapter.Description1;
                        result.Add(new DxgiAdapterInfo(
                            desc.Description,
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
                // DXGI unavailable for some reason (very old system, odd remote-session config, etc.); GPU
                // Name/DriverVersion/PnpDeviceId from WMI above still work fine without this
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

        // small local carrier for everything gathered from MSFT_PhysicalDisk + its related
        // MSFT_StorageReliabilityCounter, keyed by serial number and matched against Win32_DiskDrive below; not
        // part of the public WinStorageDriveInfo model, purely an intermediate step (same pattern as
        // DxgiAdapterInfo for GPUs)
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
            // problem: Win32_DiskDrive.InterfaceType reports "SCSI" for NVMe drives across the board, because
            // NVMe is served through the storport-based driver model, which InterfaceType still classifies
            // through its legacy SCSI-descended scheme (background:
            // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/storport-driver-overview)
            // Microsoft support directly confirms this and recommends switching to
            // MSFT_Disk/MSFT_PhysicalDisk.BusType instead:
            // https://social.msdn.microsoft.com/Forums/en-US/3cb7d1ab-e9f0-4ddb-87d4-cfee3d3915c5/the-interfacetype-of-win32diskdrive-reports-scsi-instead-of-nvme
            // Win32_DiskDrive.Model also occasionally gave a blank/garbled name for NVMe drives
            // fix: prefer the modern Storage namespace, MSFT_PhysicalDisk (FriendlyName + BusType, correctly
            // reports "NVMe"):
            // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisk
            // falls back to the legacy Win32_DiskDrive fields only when MSFT_PhysicalDisk has nothing for that
            // serial number
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

                    // reliability/SMART-style counters: Windows/Storports own abstraction over the drives raw
                    // health data
                    // GetRelated() is ManagementObjects built-in association traversal, equivalent to an
                    // "ASSOCIATORS OF" WQL query but without needing to know/construct the association class
                    // (MSFT_PhysicalDiskToStorageReliabilityCounter) or the objects WMI path string by hand
                    // fields stay null (not 0) when the controller/driver simply never reports them
                    // Confirmed field-by-field via a real Get-StorageReliabilityCounter dump 
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
                            break; // exactly one reliability counter object expected per physical disk
                        }
                    }
                    catch
                    {
                        // no reliability counter available for this disk at all (some controllers/drivers do not
                        // expose one); everything above simply stays null, same as an individually-unreported field
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
                // Storage namespace/MSFT_PhysicalDisk not available on this system: fall back to Win32_DiskDrive
                // names/bus type only, no hard failure
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

        // MSFT_PhysicalDisk.BusType is a numeric enum (STORAGE_BUS_TYPE from the Windows Driver Kit) rather than
        // a string; translated to a readable label here, with only the common consumer-hardware values mapped
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
                // excluded explicitly here, since LHMs own hardware discovery apparently never surfaces these
                // as hardware objects in the first place
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var ipProps = nic.GetIPProperties();
                var unicastAddresses = ipProps.UnicastAddresses.Select(a => a.Address).ToList();
                if (unicastAddresses.Count == 0) continue;

                // split by address family instead of showing one mixed list;
                // IPv4 and IPv6 addresses serve different purposes to whoever is reading this panel (e.g. an
                // IPv4 for LAN troubleshooting vs. an IPv6 for external reachability), and a single merged list
                // forces the reader to eyeball which is which
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

        // many LHM sensors (notably several CPU/GPU ones) only populate correctly when a NET Desktop Runtime
        // Major >= 10 is present system-wide, independent of this apps own self-contained runtime
        //
        // spawns the dotnet CLI itself instead of reading the Setup/InstalledVersions registry tree; that
        // registry tree only gets populated by the standalone SDK/Runtime installer, Microsofts own uninstall
        // tool documents that it cannot see anything installed through the Visual Studio Installer since VS2019
        // 16.3, which is exactly how a dev machine (this one included) normally gets NET, dotnet --list-runtimes
        // instead resolves against the real shared framework folders on disk and works the same regardless of
        // how NET actually got installed
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
                process.WaitForExit(2000); // startup check, never worth blocking app launch on a hung subprocess

                return output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Any(HasMajorVersion10OrNewer);
            }
            catch
            {
                return false; // dotnet not on PATH, or the process failed to start at all
            }
        }

        // a line looks like "Microsoft.WindowsDesktop.App 10.0.0 [C:\Program Files\dotnet\shared\...]", version
        // is the second whitespace-separated token
        private static bool HasMajorVersion10OrNewer(string listRuntimesLine)
        {
            var parts = listRuntimesLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && Version.TryParse(parts[1], out var version) && version.Major >= 10;
        }


        // WMI conversion
        // WMI returns loosely-typed values (often null, or a numeric type that does not exactly match what we
        // expect); these guard against both instead of throwing on a missing/oddly-typed property
        private static int ToInt(object value) => value == null ? 0 : Convert.ToInt32(value);
        private static ulong ToULong(object value) => value == null ? 0 : Convert.ToUInt64(value);
        private static bool ToBool(object value) => value != null && Convert.ToBoolean(value);
        private static uint? ToNullableUInt(object value) => value == null ? (uint?)null : Convert.ToUInt32(value);
        private static ulong? ToNullableULong(object value) => value == null ? (ulong?)null : Convert.ToUInt64(value);
    }
}
