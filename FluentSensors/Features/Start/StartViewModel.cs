using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.UI;

using FluentSensors.Common;
using FluentSensors.Common.Localization;
using FluentSensors.Common.Sensors;
using FluentSensors.Core;
using FluentSensors.Core.Lhm;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Core.Update;


namespace FluentSensors.Features.Start
{
    // the start page view model:
    // the static snapshot facts are built once and bind OneTime (WinStaticInfoService never changes after the splash);
    // the sensor counts follow the LHM discovery, the icon colour its setting, the update block UpdateService
    public class StartViewModel : INotifyPropertyChanged
    {
        // === badge colours ===

        // literals, not theme resources (like the PowerToys update badge): a status colour means the same in both
        // themes, and a white glyph on a coloured plate reads on both; the accent state uses the system accent
        private static readonly Color SuccessColor = Color.FromArgb(0xFF, 0x4C, 0xA2, 0x2E);
        private static readonly Color CautionColor = Color.FromArgb(0xFF, 0xC1, 0x8A, 0x1B);
        private static readonly Color CriticalColor = Color.FromArgb(0xFF, 0xC4, 0x3E, 0x1C);
        private static readonly Color NeutralColor = Color.FromArgb(0xFF, 0x6B, 0x6B, 0x6B);
        private static readonly Color AccentFallbackColor = Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);


        // === switches ===

        // many mainboards report no sensors, and an empty tile only takes room; (a setting candidate)
        private const bool ShowHardwareWithoutSensors = false;


        // === fields ===

        private string _updateBadgeGlyph = "";
        private Brush _updateBadgeBrush = new SolidColorBrush(NeutralColor);
        private string _updateStatusTitle = "";
        private string _updateStatusDescription = "";
        private string _releaseNotesSubtitle = "";
        private bool _isUpdateChecking;
        private bool _isUpdateActionEnabled = true;

        // the full set; the page binds the filtered SystemSnapshot
        private readonly List<SystemSnapshotEntry> _allSnapshotEntries;

        private string _sensorsFoundText = "-";
        private string _sensorsRenderedText = "-";
        private string _cpuTileValue = "-";
        private string _ramTileValue = "-";
        private string _uptimeTileValue = "-";
        private string _updateIntervalTileValue = "-";
        private string _readTimeTileValue = "-";
        private string _handlesTileValue = "-";
        private string _gcMemoryTileValue = "-";
        private string _gpuAdapterTileValue = "-";
        private string _gpuUsageTileValue = "-";
        private string _gpuMemoryTileValue = "-";
        private bool _isGpuNotDisplay;

        // process start, the splash included; utc, so daylight saving does not move the uptime
        private static readonly DateTime ProcessStartTimeUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();


        // === constructor ===

        public StartViewModel()
        {
            _allSnapshotEntries = BuildSnapshot();
            SystemSnapshot = new ObservableCollection<SystemSnapshotEntry>();

            RefreshSensorCounts();
            RefreshUpdateState();
        }


        // === bindable properties ===

        public ObservableCollection<SystemSnapshotEntry> SystemSnapshot { get; }

        public string UpdateBadgeGlyph
        {
            get => _updateBadgeGlyph;
            private set { _updateBadgeGlyph = value; OnPropertyChanged(); }
        }

        public Brush UpdateBadgeBrush
        {
            get => _updateBadgeBrush;
            private set { _updateBadgeBrush = value; OnPropertyChanged(); }
        }

        public string UpdateStatusTitle
        {
            get => _updateStatusTitle;
            private set { _updateStatusTitle = value; OnPropertyChanged(); }
        }

        public string UpdateStatusDescription
        {
            get => _updateStatusDescription;
            private set { _updateStatusDescription = value; OnPropertyChanged(); }
        }

        // under the release notes button title, e.g. "Release notes for 1.3.0"
        public string ReleaseNotesSubtitle
        {
            get => _releaseNotesSubtitle;
            private set { _releaseNotesSubtitle = value; OnPropertyChanged(); }
        }

        // a progress ring instead of the badge while a check runs
        public bool IsUpdateChecking
        {
            get => _isUpdateChecking;
            private set { _isUpdateChecking = value; OnPropertyChanged(); }
        }

        // off while a check runs
        public bool IsUpdateActionEnabled
        {
            get => _isUpdateActionEnabled;
            private set { _isUpdateActionEnabled = value; OnPropertyChanged(); }
        }


        // sensors found by LHM and sensors drawing right now, two values side by side
        public string SensorsFoundText
        {
            get => _sensorsFoundText;
            private set { _sensorsFoundText = value; OnPropertyChanged(); }
        }

        public string SensorsRenderedText
        {
            get => _sensorsRenderedText;
            private set { _sensorsRenderedText = value; OnPropertyChanged(); }
        }

        public string CpuTileValue
        {
            get => _cpuTileValue;
            private set { _cpuTileValue = value; OnPropertyChanged(); }
        }

        public string RamTileValue
        {
            get => _ramTileValue;
            private set { _ramTileValue = value; OnPropertyChanged(); }
        }

        public string UptimeTileValue
        {
            get => _uptimeTileValue;
            private set { _uptimeTileValue = value; OnPropertyChanged(); }
        }

        public string HandlesTileValue
        {
            get => _handlesTileValue;
            private set { _handlesTileValue = value; OnPropertyChanged(); }
        }

        public string GcMemoryTileValue
        {
            get => _gcMemoryTileValue;
            private set { _gcMemoryTileValue = value; OnPropertyChanged(); }
        }

        // measured against configured, as the title bar readout
        public string UpdateIntervalTileValue
        {
            get => _updateIntervalTileValue;
            private set { _updateIntervalTileValue = value; OnPropertyChanged(); }
        }

        public string ReadTimeTileValue
        {
            get => _readTimeTileValue;
            private set { _readTimeTileValue = value; OnPropertyChanged(); }
        }

        // checked once at startup, like the title bar hint
        public string PawnIoTileValue { get; } =
            AppStrings.Get(WinStaticInfoService.Instance.IsPawnIoInstalled ? "Start_PawnIoInstalled" : "Start_PawnIoMissing");

        // the graphics tile, from WinAppGpuMonitor while the page shows
        public string GpuAdapterTileValue
        {
            get => _gpuAdapterTileValue;
            private set { _gpuAdapterTileValue = value; OnPropertyChanged(); }
        }

        public string GpuUsageTileValue
        {
            get => _gpuUsageTileValue;
            private set { _gpuUsageTileValue = value; OnPropertyChanged(); }
        }

        public string GpuMemoryTileValue
        {
            get => _gpuMemoryTileValue;
            private set { _gpuMemoryTileValue = value; OnPropertyChanged(); }
        }

        // the app renders on a gpu that drives no screen, so every frame is copied across
        public bool IsGpuNotDisplay
        {
            get => _isGpuNotDisplay;
            private set { _isGpuNotDisplay = value; OnPropertyChanged(); }
        }


        // === live app status ===

        // from AppStatusService, the snapshot of the title bar readout, so both agree; (raised on the UI thread)
        public void ApplyStatus(AppStatusData data)
        {
            SensorsFoundText = data.SensorsFound.ToString();
            SensorsRenderedText = data.SensorsRendered.ToString();
            CpuTileValue = $"{data.CpuUsagePercent:0.0} %";
            RamTileValue = $"{data.RamUsageBytes / 1024.0 / 1024.0:0} MB";
            HandlesTileValue = data.HandleCount.ToString();
            GcMemoryTileValue = $"{data.GcMemoryBytes / 1024.0 / 1024.0:0.0} MB";
            UpdateIntervalTileValue = AppStrings.Format("Start_UpdateIntervalValue", data.ActualUpdateIntervalMs, data.AimedUpdateIntervalMs);
            ReadTimeTileValue = $"{data.ReadDurationMs:0} ms";

            RefreshSensorCounts();
        }

        // the first read has no usage yet, it is a rate
        public void ApplyGpu(AppGpuData data)
        {
            GpuAdapterTileValue = data.AdapterName ?? "-";
            GpuUsageTileValue = $"{data.UsagePercent:0.0} %";
            GpuMemoryTileValue = $"{data.MemoryBytes / 1024.0 / 1024.0:0} MB";
            IsGpuNotDisplay = !data.IsDisplayAdapter;
        }

        // session uptime, 2:14:37, past a day 1d 2:14:37; called more often than once a second (see
        // StartPage.UptimeTimerInterval), the property only moves with the shown second
        public void RefreshUptime()
        {
            TimeSpan uptime = DateTime.UtcNow - ProcessStartTimeUtc;

            string clock = $"{uptime.Hours}:{uptime.Minutes:00}:{uptime.Seconds:00}";
            string text = uptime.Days > 0 ? AppStrings.Format("Start_UptimeDays", uptime.Days, clock) : clock;

            if (text != UptimeTileValue) UptimeTileValue = text;
        }

        // pairs every tile with the LHM instances behind it and counts their sensors; on every tick, since
        // LhmHardwareTreeService can report a drive or adapter long after the page was built
        public void RefreshSensorCounts()
        {
            var available = LhmHardwareTreeService.Instance.HardwareGroups.ToList();

            foreach (var entry in _allSnapshotEntries)
            {
                var candidates = available.Where(g => g.Kind == entry.MatchKind).ToList();
                var matches = MatchInstances(entry, candidates);

                foreach (var match in matches) available.Remove(match);

                int count = matches.Sum(m => m.Sensors.Count);

                entry.MatchedHardwareNames = matches.Select(m => m.HardwareName).ToList();
                entry.HasSensors = count > 0;
                entry.SensorCountText = count == 0 ? AppStrings.Get("Start_SensorCountNone") : AppStrings.Plural("Start_SensorCount", count);
            }

            SyncVisibleEntries();
        }

        // SquareGridPanel lays out by index and ignores Visibility, so the list is the filter; only touched
        // when the membership changes
        private void SyncVisibleEntries()
        {
            var wanted = _allSnapshotEntries
                .Where(e => ShowHardwareWithoutSensors || e.HasSensors)
                .ToList();

            if (wanted.Count == SystemSnapshot.Count && wanted.SequenceEqual(SystemSnapshot)) return;

            SystemSnapshot.Clear();
            foreach (var entry in wanted) SystemSnapshot.Add(entry);
        }

        // a single-tile category takes every group LHM filed under it (LHM splits memory into several); a per-device
        // category claims one group each, so two GPUs never share one
        private static List<LhmHardwareInstance> MatchInstances(
            SystemSnapshotEntry entry, List<LhmHardwareInstance> candidates)
        {
            if (candidates.Count == 0) return new List<LhmHardwareInstance>();

            bool isSingleTileKind = entry.MatchKind is HardwareGroupKind.Cpu
                or HardwareGroupKind.Ram
                or HardwareGroupKind.Other;

            if (isSingleTileKind) return candidates;

            var match = candidates.Count == 1
                ? candidates[0]
                : HardwareNameMatcher.FindBestMatch(entry.MatchName, candidates, g => g.HardwareName);

            return match == null ? new List<LhmHardwareInstance>() : new List<LhmHardwareInstance> { match };
        }


        // === update state ===

        // pulled, so the page calls it on an UpdateService answer and on a theme change (the plain badge
        // brush would keep a stale accent)
        public void RefreshUpdateState()
        {
            var service = UpdateService.Instance;
            var release = service.LatestRelease;

            // before the first answer the notes of the running version
            string notesVersion = release != null && !string.IsNullOrEmpty(release.Version)
                ? release.Version
                : UpdateService.CurrentVersion;
            ReleaseNotesSubtitle = AppStrings.Format("Start_ReleaseNotesFor", UpdateService.VersionLabel(notesVersion));

            IsUpdateChecking = service.UiState == UpdateUiState.Checking;
            IsUpdateActionEnabled = service.UiState != UpdateUiState.Checking;

            string lastChecked = service.LastCheckedAt.HasValue
                ? AppStrings.Format("Start_UpdateLastChecked", service.LastCheckedAt.Value.ToString("dd.MM. HH:mm"))
                : AppStrings.Get("Start_UpdateNotChecked");

            switch (service.UiState)
            {
                case UpdateUiState.UpToDate:
                    SetBadge("", SuccessColor);
                    UpdateStatusTitle = AppStrings.Get("Start_UpdateUpToDate");
                    UpdateStatusDescription = lastChecked;
                    break;

                case UpdateUiState.Checking:
                    SetBadge("", AccentColor());
                    UpdateStatusTitle = AppStrings.Get("Start_UpdateChecking");
                    UpdateStatusDescription = "";
                    break;

                case UpdateUiState.UpdateAvailable:
                    SetBadge("", AccentColor());
                    UpdateStatusTitle = AppStrings.Get("Start_UpdateAvailable");
                    // a store update without a GitHub name has no version
                    UpdateStatusDescription = string.IsNullOrEmpty(service.Latest?.Version)
                        ? AppStrings.Get("Start_UpdateReadyUnnamed")
                        : AppStrings.Format("Start_UpdateReady", UpdateService.VersionLabel(service.Latest?.Version));
                    break;

                case UpdateUiState.Skipped:
                    SetBadge("", CautionColor);
                    UpdateStatusTitle = AppStrings.Format("Start_UpdateSkipped", UpdateService.VersionLabel(service.SkippedVersion));
                    UpdateStatusDescription = AppStrings.Get("Start_UpdateSkippedHint");
                    break;

                case UpdateUiState.Failed:
                    SetBadge("", CriticalColor);
                    UpdateStatusTitle = AppStrings.Get("Start_UpdateFailed");
                    UpdateStatusDescription = AppDistribution.SupportsSelfUpdate
                        ? AppStrings.Get("Start_UpdateFailedGitHub")
                        : AppStrings.Get("Start_UpdateFailedStore");
                    break;

                default:
                    SetBadge("", NeutralColor);
                    UpdateStatusTitle = AppStrings.Get("Start_UpdateCheck");
                    UpdateStatusDescription = lastChecked;
                    break;
            }
        }

        private void SetBadge(string glyph, Color color)
        {
            UpdateBadgeGlyph = glyph;
            UpdateBadgeBrush = new SolidColorBrush(color);
        }

        // the Windows accent, else the WinUI default
        private static Color AccentColor() =>
            Application.Current.Resources.TryGetValue("SystemAccentColor", out object value) && value is Color color
                ? color
                : AccentFallbackColor;


        // === snapshot ===

        // in HardwareGroupKind order, like the sensor list and the hardware view
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

        // icon, colour and label from HardwareGroupInfo, like the sensor list and the hardware view; a "-" from the
        // formatters is dropped, not shown
        private static SystemSnapshotEntry Row(HardwareGroupKind kind, string title, params string[] details)
        {
            var profile = HardwareGroupInfo.GetProfile(kind);

            return LabelledRow(kind, profile.IconGlyph, profile.Label, title, details);
        }

        // not an overload of Row: one more string would match Row(kind, title, detail, detail) without expanding params
        // and shift every argument; the glyph comes in, a network tile picks its own
        private static SystemSnapshotEntry LabelledRow(HardwareGroupKind kind, string iconGlyph, string category, string title, params string[] details)
        {
            return new SystemSnapshotEntry(
                iconGlyph,
                HardwareGroupInfo.GetIconBrush(kind),
                category,
                string.IsNullOrWhiteSpace(title) ? AppStrings.Get("Start_Unknown") : title,
                details.Where(d => !string.IsNullOrWhiteSpace(d) && d != "-").ToList(),
                kind,
                title ?? "");
        }


        // re-resolves the tile icons after the icon colour setting flipped; the list stays
        public void RefreshIconBrushes()
        {
            foreach (var entry in _allSnapshotEntries)
            {
                entry.IconBrush = HardwareGroupInfo.GetIconBrush(entry.MatchKind);
            }
        }


        // === one adder per category ===

        // the processor name comes from LHM, like in the hardware view (WinCpuInfo has none)
        private static void AddCpu(List<SystemSnapshotEntry> rows, WinStaticInfoService info)
        {
            var cpu = info.Cpu;

            string name = LhmHardwareTreeService.Instance.HardwareGroups
                .FirstOrDefault(g => g.Kind == HardwareGroupKind.Cpu)?.HardwareName ?? "";

            string cores = cpu.PhysicalCores > 0 && cpu.LogicalProcessors > 0
                ? $"{AppTerms.Plural("Start_Cores", cpu.PhysicalCores)} / {AppTerms.Plural("Start_Threads", cpu.LogicalProcessors)}"
                : "";

            string clock = cpu.MaxClockSpeedMhz > 0
                ? HardwareInfoFormatter.FormatMhz((uint)cpu.MaxClockSpeedMhz)
                : "";

            // level 5 is L3 in the raw WMI numbering, see HardwareInfoFormatter
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

            // the first module speaks for the set; (a mixed kit is rare)
            var first = memory.Modules[0];

            string name = $"{first.Manufacturer} {first.PartNumber}".Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "Memory";

            string slots = memory.TotalSlots > 0
                ? AppTerms.Format("Start_SlotsUsed", memory.Modules.Count, memory.TotalSlots)
                : AppTerms.Plural("Start_Modules", memory.Modules.Count);

            rows.Add(Row(
                HardwareGroupKind.Ram,
                name,
                AppTerms.Format("Start_MemoryTotal", HardwareInfoFormatter.FormatBytesAsGb(total)),
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

                // where its picture goes, so a hybrid laptop shows which gpu drives the display; (the driver version is
                // on the gpu page)
                string display = HardwareInfoFormatter.FormatGpuDisplay(gpu);
                if (display.Length > 0) display = AppTerms.Format("Start_GpuDisplay", display);

                rows.Add(Row(
                    HardwareGroupKind.Gpu,
                    gpu.Name,
                    HardwareInfoFormatter.FormatVendorName(gpu.VendorId),
                    memory,
                    display));
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

                // the glyph depends on the device, wi-fi for a wireless adapter
                rows.Add(LabelledRow(
                    HardwareGroupKind.Network,
                    HardwareGroupInfo.GetNetworkIconGlyph(adapter.InterfaceType),
                    HardwareGroupInfo.GetProfile(HardwareGroupKind.Network).Label,
                    adapter.Name,
                    HardwareInfoFormatter.FormatInterfaceType(adapter.InterfaceType),
                    speed));
            }
        }

        // LHM files a motherboard under Other; the row gets the label "Motherboard"
        private static void AddMotherboard(List<SystemSnapshotEntry> rows, WinStaticInfoService info)
        {
            var board = info.Motherboard;

            string name = $"{board.Manufacturer} {board.Product}".Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            string bios = string.IsNullOrWhiteSpace(board.BiosVersion) ? "" : $"BIOS {board.BiosVersion}";

            rows.Add(LabelledRow(
                HardwareGroupKind.Other,
                HardwareGroupInfo.GetProfile(HardwareGroupKind.Other).IconGlyph,
                "Motherboard",
                name,
                bios,
                board.BiosReleaseDate));
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
