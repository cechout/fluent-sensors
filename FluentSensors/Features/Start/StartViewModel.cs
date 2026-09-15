using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using System.Linq;

using FluentSensors.Common.Sensors;
using FluentSensors.Core.Lhm;
using FluentSensors.Core.StaticInfo;


namespace FluentSensors.Features.Start
{
    // backs the start page
    //
    // the snapshot is built once per page instance and never refreshed: WinStaticInfoService resolves the whole
    // machine during the splash and states plainly that none of it changes afterwards, so there is nothing to
    // notify about and every row binds OneTime
    public class StartViewModel
    {
        // === constructor ===

        public StartViewModel()
        {
            SystemSnapshot = BuildSnapshot();
        }


        // === bindable properties ===

        public IReadOnlyList<SystemSnapshotEntry> SystemSnapshot { get; }

        // === snapshot ===

        // ordered the way HardwareGroupKind itself is ordered, so the snapshot reads in the same sequence as the
        // sensor list and the hardware view
        private static List<SystemSnapshotEntry> BuildSnapshot()
        {
            var info = WinStaticInfoService.Instance;
            var rows = new List<SystemSnapshotEntry>();

            AddCpu(rows, info);
            AddMemory(rows, info);
            AddGpus(rows, info);
            AddDrives(rows, info);
            AddAdapters(rows, info);
            AddMotherboard(rows, info);

            return rows;
        }

        // the icon, colour and category label all come from HardwareGroupInfo, the same profile the sensor list
        // and the hardware view draw from, so one category never looks like two different things
        //
        // the formatters answer "-" for anything this machine does not report; those are dropped here rather
        // than rendered, a snapshot row should not show a bare dash where a value belongs
        private static SystemSnapshotEntry Row(HardwareGroupKind kind, string title, params string[] details)
        {
            var profile = HardwareGroupInfo.GetProfile(kind);

            return new SystemSnapshotEntry(
                profile.IconGlyph,
                new SolidColorBrush(profile.Color),
                profile.Label,
                string.IsNullOrWhiteSpace(title) ? "Unknown" : title,
                details.Where(d => !string.IsNullOrWhiteSpace(d) && d != "-").ToList());
        }


        // === one adder per category ===

        // the processor name is the single fact WinCpuInfo does not carry, so it comes from LHM here, exactly
        // as the hardware view resolves it
        private static void AddCpu(List<SystemSnapshotEntry> rows, WinStaticInfoService info)
        {
            var cpu = info.Cpu;

            string name = LhmHardwareTreeService.Instance.HardwareGroups
                .FirstOrDefault(g => g.Kind == HardwareGroupKind.Cpu)?.HardwareName ?? "";

            string cores = cpu.PhysicalCores > 0 && cpu.LogicalProcessors > 0
                ? $"{cpu.PhysicalCores} cores / {cpu.LogicalProcessors} threads"
                : "";

            string clock = cpu.MaxClockSpeedMhz > 0
                ? HardwareInfoFormatter.FormatMhz((uint)cpu.MaxClockSpeedMhz)
                : "";

            // level 5 is L3 in the raw WMI numbering WinCpuCacheEntry keeps, see HardwareInfoFormatter
            string cache = HardwareInfoFormatter.FormatCacheLevelTotal(cpu.CacheEntries, level: 5);
            if (cache != "-") cache = $"{cache} L3";

            rows.Add(Row(HardwareGroupKind.Cpu, name, cores, clock, cache));
        }

        private static void AddMemory(List<SystemSnapshotEntry> rows, WinStaticInfoService info)
        {
            var memory = info.Memory;
            if (memory.Modules.Count == 0) return;

            ulong total = 0;
            foreach (var module in memory.Modules) total += module.CapacityBytes;

            // a mixed kit is possible but rare; the first module is taken as speaking for the set rather than
            // listing every stick on a page that is meant to be an overview
            var first = memory.Modules[0];

            string name = $"{first.Manufacturer} {first.PartNumber}".Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "Memory";

            string slots = memory.TotalSlots > 0
                ? $"{memory.Modules.Count} of {memory.TotalSlots} slots"
                : $"{memory.Modules.Count} modules";

            rows.Add(Row(
                HardwareGroupKind.Ram,
                name,
                $"{HardwareInfoFormatter.FormatBytesAsGb(total)} total",
                slots,
                HardwareInfoFormatter.FormatMemoryType(first.SmbiosMemoryType),
                HardwareInfoFormatter.FormatMemorySpeed(first.ConfiguredClockSpeedMhz)));
        }

        private static void AddGpus(List<SystemSnapshotEntry> rows, WinStaticInfoService info)
        {
            foreach (var gpu in info.Gpus)
            {
                string memory = gpu.DedicatedVideoMemoryBytes > 0
                    ? HardwareInfoFormatter.FormatBytesAsGb(gpu.DedicatedVideoMemoryBytes)
                    : "";

                string driver = string.IsNullOrWhiteSpace(gpu.DriverVersion)
                    ? ""
                    : $"Driver {gpu.DriverVersion}";

                rows.Add(Row(
                    HardwareGroupKind.Gpu,
                    gpu.Name,
                    HardwareInfoFormatter.FormatVendorName(gpu.VendorId),
                    memory,
                    driver));
            }
        }

        private static void AddDrives(List<SystemSnapshotEntry> rows, WinStaticInfoService info)
        {
            foreach (var drive in info.Drives)
            {
                rows.Add(Row(
                    HardwareGroupKind.Storage,
                    drive.FriendlyName,
                    HardwareInfoFormatter.FormatBytesAsGb(drive.SizeBytes),
                    drive.BusType));
            }
        }

        private static void AddAdapters(List<SystemSnapshotEntry> rows, WinStaticInfoService info)
        {
            foreach (var adapter in info.NetworkAdapters)
            {
                string speed = adapter.SpeedBitsPerSecond > 0
                    ? HardwareInfoFormatter.FormatBitsPerSecond(adapter.SpeedBitsPerSecond)
                    : "";

                rows.Add(Row(
                    HardwareGroupKind.Network,
                    adapter.Name,
                    HardwareInfoFormatter.FormatInterfaceType(adapter.InterfaceType),
                    speed));
            }
        }

        // LHM files a motherboard under HardwareGroupKind.Other, which labels it "Other"; that is right for a
        // sensor group and wrong for a snapshot row, so only the label is swapped out
        private static void AddMotherboard(List<SystemSnapshotEntry> rows, WinStaticInfoService info)
        {
            var board = info.Motherboard;

            string name = $"{board.Manufacturer} {board.Product}".Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            string bios = string.IsNullOrWhiteSpace(board.BiosVersion) ? "" : $"BIOS {board.BiosVersion}";

            var row = Row(HardwareGroupKind.Other, name, bios, board.BiosReleaseDate);
            rows.Add(row with { Category = "Motherboard" });
        }

    }
}
