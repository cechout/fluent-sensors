using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using Microsoft.Win32;
using System.Net.NetworkInformation;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortice.DXGI;
using System.Net.Sockets;

using FluentSensors.Common.Localization;


namespace FluentSensors.Core.StaticInfo
{
    // static hardware info:
    // facts from Windows itself (WMI and Win32), queried once on first access, unlike the live
    // LHM sensors; a lazy singleton
    // gpus, drives, network adapters and batteries can move while the app runs and are asked again, see the refresh
    // section
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
            Batteries = WinBatteryReader.QueryBatteries();
            Displays = QueryDisplays();
            Motherboard = QueryMotherboard();
            DotNetRuntimeState = !PretendPrerequisitesMissing && QueryDotNetRuntimeInstalled()
                ? WinPrerequisiteState.Installed
                : WinPrerequisiteState.Missing;
            PawnIoState = !PretendPrerequisitesMissing && QueryPawnIoInstalled()
                ? WinPrerequisiteState.Installed
                : WinPrerequisiteState.Missing;
        }


        // === switches ===

        // true starts with both prerequisites missing whatever the machine has, to look at the hints; coming back
        // to the main window then finds the real ones and shows the restart state
        private const bool PretendPrerequisitesMissing = false;


        // === public binding surface ===
        // (no INotifyPropertyChanged; cpu, memory and motherboard never change after construction, the five lists
        // are swapped whole by a refresh, which raises the matching event)

        public WinCpuInfo Cpu { get; }
        public IReadOnlyList<WinGpuInfo> Gpus { get; private set; }
        public WinMemoryInfo Memory { get; }
        public IReadOnlyList<WinStorageDriveInfo> Drives { get; private set; }
        public IReadOnlyList<WinNetworkAdapterInfo> NetworkAdapters { get; private set; }
        public IReadOnlyList<WinBatteryInfo> Batteries { get; private set; }
        public IReadOnlyList<WinDisplayInfo> Displays { get; private set; }
        public WinMotherboardInfo Motherboard { get; }

        // installed once any Major >= 10 shared framework version is found, see QueryDotNetRuntimeInstalled
        public WinPrerequisiteState DotNetRuntimeState { get; private set; }

        // installed while the PawnIO kernel driver is present, see QueryPawnIoInstalled
        public WinPrerequisiteState PawnIoState { get; private set; }


        // === refresh ===

        // each asks Windows again off the calling thread, then swaps its list and raises its event back on it; called
        // from the UI thread only (WinStaticInfoRefresher), so a consumer never sees a list change under it
        // a device that is gone leaves its list, like its LHM instance leaves the hardware tree

        public event Action? GpusChanged;
        public event Action? DrivesChanged;
        public event Action? NetworkAdaptersChanged;
        public event Action? BatteriesChanged;
        public event Action? DisplaysChanged;
        public event Action? PrerequisitesChanged;

        // the gpu list, the driver versions and the display each gpu drives
        public async Task RefreshGpusAsync()
        {
            var fresh = await QueryChangedAsync(Gpus, QueryGpus);
            if (fresh == null) return;

            Gpus = fresh;
            GpusChanged?.Invoke();
        }

        // the drive list and the reliability counters (temperature, power on hours, errors, wear)
        public async Task RefreshDrivesAsync()
        {
            var fresh = await QueryChangedAsync(Drives, QueryDrives);
            if (fresh == null) return;

            Drives = fresh;
            DrivesChanged?.Invoke();
        }

        // the adapters that are up, their link speed and addresses
        public async Task RefreshNetworkAdaptersAsync()
        {
            var fresh = await QueryChangedAsync(NetworkAdapters, QueryNetworkAdapters);
            if (fresh == null) return;

            NetworkAdapters = fresh;
            NetworkAdaptersChanged?.Invoke();
        }

        // the batteries that are present, their full capacity and cycle count
        public async Task RefreshBatteriesAsync()
        {
            var fresh = await QueryChangedAsync(Batteries, WinBatteryReader.QueryBatteries);
            if (fresh == null) return;

            Batteries = fresh;
            BatteriesChanged?.Invoke();
        }

        // the active displays, their mode, HDR state and what the monitor says over DDC/CI
        public async Task RefreshDisplaysAsync()
        {
            var fresh = await QueryChangedAsync(Displays, QueryDisplays);
            if (fresh == null) return;

            Displays = fresh;
            DisplaysChanged?.Invoke();
        }

        private static List<WinDisplayInfo> QueryDisplays() => WinDisplayReader.QueryDisplays(includeDetails: true);

        // looks again for what was missing at startup; found now means installed while the app runs, and LHM needs a
        // restart to use it
        public async Task RefreshPrerequisitesAsync()
        {
            bool dotNetArrived = DotNetRuntimeState == WinPrerequisiteState.Missing && await Task.Run(QueryDotNetRuntimeInstalled);
            bool pawnIoArrived = PawnIoState == WinPrerequisiteState.Missing && await Task.Run(QueryPawnIoRegistered);
            if (!dotNetArrived && !pawnIoArrived) return;

            if (dotNetArrived) DotNetRuntimeState = WinPrerequisiteState.RestartRequired;
            if (pawnIoArrived) PawnIoState = WinPrerequisiteState.RestartRequired;
            PrerequisitesChanged?.Invoke();
        }

        // the fresh list; null when the query failed or nothing differs
        private static async Task<List<T>?> QueryChangedAsync<T>(IReadOnlyList<T> current, Func<List<T>> query)
        {
            List<T> fresh;
            try
            {
                fresh = await Task.Run(query);
            }
            catch
            {
                return null; // a failed WMI query keeps the last list
            }

            // by content; the records hold lists (addresses, displays), which their own equality compares by reference
            return JsonSerializer.Serialize(fresh) == JsonSerializer.Serialize(current.ToList()) ? null : fresh;
        }


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
                    Description: item["Description"]?.ToString() ?? "",
                    MicrocodeRevision: QueryMicrocodeRevision(),
                    InstructionSets: QueryInstructionSets(),
                    CoreTopology: topology,
                    CacheEntries: cacheEntries
                );
            }

            // no Win32_Processor row; the topology still goes back
            return new WinCpuInfo(
                0, 0, 0, "", false, false, "", QueryMicrocodeRevision(), QueryInstructionSets(), topology, new List<WinCpuCacheEntry>());
        }

        // the "Update Revision" Windows keeps per processor: eight bytes with the revision in the upper half on
        // Intel and in the lower half on AMD, or only the four of the revision
        private static uint? QueryMicrocodeRevision()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (key?.GetValue("Update Revision") is not byte[] { Length: >= 4 } bytes) return null;

                uint upper = bytes.Length >= 8 ? BitConverter.ToUInt32(bytes, 4) : 0;
                uint revision = upper != 0 ? upper : BitConverter.ToUInt32(bytes, 0);
                return revision != 0 ? revision : null;
            }
            catch
            {
                return null; // no access to the key
            }
        }

        // what the .NET runtime found on this cpu; the short list a user compares against the requirements of a game
        // or an encoder, not every cpuid bit
        private static List<string> QueryInstructionSets()
        {
            var result = new List<string>();
            void Add(bool isSupported, string name) { if (isSupported) result.Add(name); }

            Add(Sse42.IsSupported, "SSE4.2");
            Add(Aes.IsSupported, "AES");
            Add(Avx.IsSupported, "AVX");
            Add(Fma.IsSupported, "FMA3");
            Add(Avx2.IsSupported, "AVX2");
            Add(Bmi2.IsSupported, "BMI2");
            Add(AvxVnni.IsSupported, "AVX-VNNI");
            Add(Avx512F.IsSupported, "AVX-512");

            return result;
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
                string pnpDeviceId = item["PNPDeviceID"]?.ToString() ?? "";

                // best-effort match against DXGI; its Description is near-identical to the WMI Name but not
                // byte-identical, so HardwareNameMatcher
                var dxgiMatch = HardwareNameMatcher.FindBestMatch(name, dxgiAdapters, a => a.Description);

                result.Add(new WinGpuInfo(
                    Name: name,
                    DriverVersion: item["DriverVersion"]?.ToString() ?? "",
                    DriverDate: ToIsoDate(item["DriverDate"]),
                    PnpDeviceId: pnpDeviceId,
                    PciLink: WinPciLinkReader.Query(pnpDeviceId),
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
            ulong maxCapacityKb = 0;
            uint errorCorrection = 0;
            using (var arraySearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemoryArray"))
            {
                foreach (ManagementObject item in arraySearcher.Get())
                {
                    totalSlots += ToInt(item["MemoryDevices"]);

                    // MaxCapacity is 32 bit and ends at 2 TB; the Ex field is newer and can be missing
                    ulong capacityKb = ToULong(item["MaxCapacityEx"]);
                    maxCapacityKb += capacityKb > 0 ? capacityKb : ToULong(item["MaxCapacity"]);

                    if (errorCorrection == 0) errorCorrection = (uint)ToInt(item["MemoryErrorCorrection"]);
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

            return new WinMemoryInfo(totalSlots, maxCapacityKb * 1024, errorCorrection, modules);
        }

        // MSFT_PhysicalDisk and its MSFT_StorageReliabilityCounter, keyed by serial and matched to Win32_DiskDrive
        // below; an intermediate step like DxgiAdapterInfo
        private record PhysicalDiskExtraInfo(
            string FriendlyName,
            string BusType,
            uint? MediaType,
            uint? SpindleSpeedRpm,
            uint? HealthStatus,
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

                    // the spindle speed is all ones when the drive does not tell
                    uint? spindleSpeed = ToNullableUInt(item["SpindleSpeed"]);
                    if (spindleSpeed == uint.MaxValue) spindleSpeed = null;

                    physicalDiskInfo[serial] = new PhysicalDiskExtraInfo(
                        friendly, busType,
                        ToNullableUInt(item["MediaType"]), spindleSpeed, ToNullableUInt(item["HealthStatus"]),
                        temperature, temperatureMax, wear, powerOnHours,
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

            var disks = QueryDisks();

            using var driveSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (ManagementObject item in driveSearcher.Get())
            {
                string serial = item["SerialNumber"]?.ToString()?.Trim() ?? "";
                string legacyModel = item["Model"]?.ToString()?.Trim() ?? "";
                string legacyBusType = item["InterfaceType"]?.ToString() ?? "";
                string pnpDeviceId = item["PNPDeviceID"]?.ToString() ?? "";

                physicalDiskInfo.TryGetValue(serial, out var modern);
                disks.TryGetValue(ToInt(item["Index"]), out var disk);

                string busType = !string.IsNullOrEmpty(modern?.BusType) ? modern.BusType : legacyBusType;

                result.Add(new WinStorageDriveInfo(
                    FriendlyName: !string.IsNullOrEmpty(modern?.FriendlyName) ? modern.FriendlyName : legacyModel,
                    SerialNumber: serial,
                    FirmwareRevision: item["FirmwareRevision"]?.ToString()?.Trim() ?? "",
                    BusType: busType,
                    SizeBytes: ToULong(item["Size"]),
                    PnpDeviceId: pnpDeviceId,
                    MediaType: modern?.MediaType,
                    SpindleSpeedRpm: modern?.SpindleSpeedRpm,
                    HealthStatus: modern?.HealthStatus,
                    PartitionStyle: disk?.PartitionStyle,
                    PartitionCount: disk?.PartitionCount,
                    IsBootDisk: disk?.IsBoot ?? false,
                    IsSystemDisk: disk?.IsSystem ?? false,
                    Volumes: disk?.Volumes ?? new List<WinVolumeInfo>(),

                    // the disk is a child of its controller, which holds the link; only asked for NVMe, the link of a
                    // SATA controller says nothing about the drive
                    PciLink: busType == "NVMe" ? WinPciLinkReader.Query(pnpDeviceId, parentLevels: 1) : null,
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
                    FlushLatencyMaxMs: modern?.FlushLatencyMaxMs,
                    DiskNumber: ToInt(item["Index"])
                ));
            }

            return result;
        }

        // MSFT_Disk and the lettered volumes of its MSFT_Partition entries, keyed by disk number
        // (Win32_DiskDrive.Index); an intermediate step like PhysicalDiskExtraInfo
        private record DiskLayoutInfo(
            uint? PartitionStyle,
            uint? PartitionCount,
            bool IsBoot,
            bool IsSystem,
            List<WinVolumeInfo> Volumes);

        private static Dictionary<int, DiskLayoutInfo> QueryDisks()
        {
            var result = new Dictionary<int, DiskLayoutInfo>();

            try
            {
                using (var diskSearcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_Disk"))
                {
                    foreach (ManagementObject item in diskSearcher.Get())
                    {
                        result[ToInt(item["Number"])] = new DiskLayoutInfo(
                            ToNullableUInt(item["PartitionStyle"]),
                            ToNullableUInt(item["NumberOfPartitions"]),
                            ToBool(item["IsBoot"]),
                            ToBool(item["IsSystem"]),
                            new List<WinVolumeInfo>());
                    }
                }

                using var partitionSearcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage", "SELECT DiskNumber, DriveLetter FROM MSFT_Partition");
                foreach (ManagementObject item in partitionSearcher.Get())
                {
                    // the letter is a character, 0 for a partition without one (recovery, EFI)
                    char letter = item["DriveLetter"] is char c ? c : '\0';
                    if (!char.IsLetter(letter)) continue;
                    if (!result.TryGetValue(ToInt(item["DiskNumber"]), out var disk)) continue;

                    var volume = QueryVolume(letter);
                    if (volume != null) disk.Volumes.Add(volume);
                }
            }
            catch
            {
                // no Storage namespace; the drives keep no layout
            }

            return result;
        }

        // file system, size and free space of one drive letter; null while the volume is locked or not ready
        private static WinVolumeInfo? QueryVolume(char letter)
        {
            try
            {
                var drive = new DriveInfo(letter.ToString());
                if (!drive.IsReady) return null;

                return new WinVolumeInfo($"{letter}:", drive.DriveFormat, (ulong)drive.TotalSize, (ulong)drive.TotalFreeSpace);
            }
            catch
            {
                return null;
            }
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
            List<WinWlanConnection>? wlanConnections = null; // asked once, and only with a Wi-Fi adapter up

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

                WinWlanConnection? wlan = null;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && Guid.TryParse(nic.Id, out Guid interfaceId))
                {
                    wlanConnections ??= WinWlanReader.QueryConnections();
                    wlan = wlanConnections.FirstOrDefault(c => c.InterfaceId == interfaceId);
                }

                result.Add(new WinNetworkAdapterInfo(
                    Name: nic.Name,
                    Description: nic.Description,
                    MacAddress: nic.GetPhysicalAddress().ToString(),
                    SpeedBitsPerSecond: nic.Speed,
                    InterfaceType: nic.NetworkInterfaceType,
                    IPv4Addresses: ipv4Addresses,
                    IPv6Addresses: ipv6Addresses,
                    DhcpEnabled: ipProps.DhcpServerAddresses.Count > 0,
                    Gateways: ipProps.GatewayAddresses.Select(g => g.Address.ToString()).ToList(),
                    DnsServers: ipProps.DnsAddresses.Select(a => a.ToString()).ToList(),
                    DhcpServers: ipProps.DhcpServerAddresses.Select(a => a.ToString()).ToList(),
                    Driver: QueryNetworkDriver(nic.Id),
                    Wlan: wlan != null ? new WinWlanInfo(wlan.PhyType, wlan.AuthAlgorithm, wlan.Channel, wlan.FrequencyMhz) : null
                ));
            }

            return result;
        }

        // the driver per adapter id, kept for the session: the adapter list is asked every few seconds and a WMI
        // round trip each time would be wasted on facts that only move with a driver update
        private static readonly Dictionary<string, WinNetworkDriverInfo?> _networkDrivers = new(StringComparer.OrdinalIgnoreCase);

        private static WinNetworkDriverInfo? QueryNetworkDriver(string adapterId)
        {
            lock (_networkDrivers)
            {
                if (_networkDrivers.TryGetValue(adapterId, out var known)) return known;

                // an adapter not seen before; MSFT_NetAdapter lists them all, so one query fills the rest too
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        @"root\StandardCimv2", "SELECT InterfaceGuid, DriverVersionString, DriverDate, DriverProvider FROM MSFT_NetAdapter");
                    foreach (ManagementObject item in searcher.Get())
                    {
                        string id = item["InterfaceGuid"]?.ToString() ?? "";
                        if (id.Length == 0) continue;

                        // DriverDate is plain text here ("2026-05-20"), not a WMI datetime
                        _networkDrivers[id] = new WinNetworkDriverInfo(
                            item["DriverVersionString"]?.ToString()?.Trim() ?? "",
                            item["DriverDate"]?.ToString()?.Trim() ?? "",
                            item["DriverProvider"]?.ToString()?.Trim() ?? "");
                    }
                }
                catch
                {
                    // no StandardCimv2 namespace; the adapters keep no driver line
                }

                // also an adapter the class does not list, so it is not asked for again
                if (!_networkDrivers.ContainsKey(adapterId)) _networkDrivers[adapterId] = null;
                return _networkDrivers[adapterId];
            }
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

            string biosVersion = "", biosDate = "", embeddedControllerVersion = "";
            using (var biosSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS"))
            {
                foreach (ManagementObject item in biosSearcher.Get())
                {
                    biosVersion = item["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? "";
                    biosDate = ToIsoDate(item["ReleaseDate"]);

                    // both 255 on a board without an embedded controller (SMBIOS Type 0)
                    int major = item["EmbeddedControllerMajorVersion"] == null ? 255 : ToInt(item["EmbeddedControllerMajorVersion"]);
                    int minor = item["EmbeddedControllerMinorVersion"] == null ? 255 : ToInt(item["EmbeddedControllerMinorVersion"]);
                    if (major != 255 || minor != 255) embeddedControllerVersion = $"{major}.{minor}";
                    break;
                }
            }

            string systemFamily = "", systemModel = "";
            try
            {
                using var systemSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                foreach (ManagementObject item in systemSearcher.Get())
                {
                    systemFamily = item["SystemFamily"]?.ToString()?.Trim() ?? "";
                    systemModel = item["Model"]?.ToString()?.Trim() ?? "";
                    break;
                }
            }
            catch
            {
                // no SystemFamily before Windows 10; both stay blank
            }

            uint chassisType = 0;
            try
            {
                using var enclosureSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_SystemEnclosure");
                foreach (ManagementObject item in enclosureSearcher.Get())
                {
                    if (item["ChassisTypes"] is ushort[] { Length: > 0 } types) chassisType = types[0];
                    break;
                }
            }
            catch
            {
                // stays unknown
            }

            return new WinMotherboardInfo(
                manufacturer, product, version, biosVersion, biosDate,
                systemFamily, systemModel, chassisType, embeddedControllerVersion,
                QuerySecureBootEnabled(), QueryTpmVersion());
        }

        // the state Windows keeps of the UEFI variable; the key is missing on a legacy BIOS
        private static bool? QuerySecureBootEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
                return key?.GetValue("UEFISecureBootEnabled") is int value ? value != 0 : null;
            }
            catch
            {
                return null;
            }
        }

        // the first part of Win32_Tpm.SpecVersion ("2.0, 0, 1.38"); "" without a TPM or without the right to ask
        private static string QueryTpmVersion()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\cimv2\Security\MicrosoftTpm", "SELECT SpecVersion FROM Win32_Tpm");
                foreach (ManagementObject item in searcher.Get())
                {
                    string specVersion = item["SpecVersion"]?.ToString() ?? "";
                    return specVersion.Split(',')[0].Trim();
                }
            }
            catch
            {
                // no TPM namespace
            }

            return "";
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

        // the same look LHM takes, for asking again: PawnIo.IsInstalled reads the uninstall entry once in its static
        // constructor and never changes afterwards
        private static bool QueryPawnIoRegistered()
        {
            const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

            try
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = baseKey.OpenSubKey(UninstallKey);
                    if (Version.TryParse(key?.GetValue("DisplayVersion") as string, out _)) return true;
                }
            }
            catch
            {
                // no access to the key; stays missing
            }

            return false;
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

        // a WMI datetime ("20260917000000.000000-000") as "2026-09-17"; only its date part, so no time zone shifts it
        private static string ToIsoDate(object value)
        {
            string text = value?.ToString() ?? "";
            if (text.Length < 8 || !text.AsSpan(0, 8).ToString().All(char.IsDigit)) return "";

            return $"{text[..4]}-{text[4..6]}-{text[6..8]}";
        }
    }
}
