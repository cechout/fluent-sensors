using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.UI;

using FluentSensors.Common.Sensors;
using FluentSensors.Core;
using FluentSensors.Core.Lhm;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Core.Update;


namespace FluentSensors.Features.Start
{
    // backs the start page
    //
    // the static facts of the snapshot are built once per page instance and never refreshed: WinStaticInfoService
    // resolves the whole machine during the splash and states plainly that none of it changes afterwards, so
    // those bind OneTime
    // the sensor count per tile is the exception and moves with LHMs ongoing discovery, and the update block
    // moves whenever UpdateService answers
    public class StartViewModel : INotifyPropertyChanged
    {
        // === badge colours ===

        // literal rather than theme resources, the same choice PowerToys makes for its own update badge: a status
        // colour means the same thing in light and dark, and a coloured plate with a white glyph reads correctly
        // against both
        // the accent state is the exception and comes from the system accent, which is a user setting rather than
        // a theme one
        private static readonly Color SuccessColor = Color.FromArgb(0xFF, 0x4C, 0xA2, 0x2E);
        private static readonly Color CautionColor = Color.FromArgb(0xFF, 0xC1, 0x8A, 0x1B);
        private static readonly Color CriticalColor = Color.FromArgb(0xFF, 0xC4, 0x3E, 0x1C);
        private static readonly Color NeutralColor = Color.FromArgb(0xFF, 0x6B, 0x6B, 0x6B);
        private static readonly Color AccentFallbackColor = Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);


        // === switches ===

        // a mainboard reports no sensors at all on plenty of systems, and a tile with nothing to say is only
        // taking up room; later a setting
        private const bool ShowHardwareWithoutSensors = false;


        // === fields ===

        private string _updateBadgeGlyph = "";
        private Brush _updateBadgeBrush = new SolidColorBrush(NeutralColor);
        private string _updateStatusTitle = "";
        private string _updateStatusDescription = "";
        private string _releaseNotesSubtitle = "";
        private bool _isUpdateChecking;
        private bool _isUpdateActionEnabled = true;

        // the full set; SystemSnapshot below is the filtered view of it that the page actually binds to
        private readonly List<SystemSnapshotEntry> _allSnapshotEntries;

        private string _sensorsFoundText = "-";
        private string _sensorsRenderedText = "-";
        private string _cpuTileValue = "-";
        private string _ramTileValue = "-";


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

        // what the release notes button says underneath its title, e.g. "Release notes for 1.3.0"
        public string ReleaseNotesSubtitle
        {
            get => _releaseNotesSubtitle;
            private set { _releaseNotesSubtitle = value; OnPropertyChanged(); }
        }

        // swaps the badge for a progress ring while a check is in flight
        public bool IsUpdateChecking
        {
            get => _isUpdateChecking;
            private set { _isUpdateChecking = value; OnPropertyChanged(); }
        }

        // a store build has nothing for the button to do, the store owns updates there
        public bool IsUpdateActionEnabled
        {
            get => _isUpdateActionEnabled;
            private set { _isUpdateActionEnabled = value; OnPropertyChanged(); }
        }


        // how many sensors LHM found, against how many are actually drawing right now; two numbers rather than
        // one string, they sit side by side as their own values
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


        // === live app status ===

        // fed from AppStatusService, the same snapshot the title bar readout already runs on, so both always
        // agree; the service raises it from the UI thread, so nothing is dispatched here
        public void ApplyStatus(AppStatusData data)
        {
            SensorsFoundText = data.SensorsFound.ToString();
            SensorsRenderedText = data.SensorsRendered.ToString();
            CpuTileValue = $"{data.CpuUsagePercent:0.0} %";
            RamTileValue = $"{data.RamUsageBytes / 1024.0 / 1024.0:0} MB";

            RefreshSensorCounts();
        }

        // pairs every snapshot tile with the LHM instance or instances that report its sensors, then reads the
        // count off them
        //
        // re-resolved on every tick rather than bound once, because LhmHardwareTreeService fills in gradually and
        // can still report a drive or an adapter for the first time long after this page was built
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
                entry.SensorCountText = count switch
                {
                    0 => "No sensors",
                    1 => "1 sensor",
                    _ => $"{count} sensors"
                };
            }

            SyncVisibleEntries();
        }

        // SquareGridPanel measures and arranges every child by raw index and never looks at Visibility, so a
        // collapsed tile would still hold its cell open; the list itself has to be the filter
        //
        // only touched when the membership actually changes, otherwise the whole row would be rebuilt on every
        // poll tick
        private void SyncVisibleEntries()
        {
            var wanted = _allSnapshotEntries
                .Where(e => ShowHardwareWithoutSensors || e.HasSensors)
                .ToList();

            if (wanted.Count == SystemSnapshot.Count && wanted.SequenceEqual(SystemSnapshot)) return;

            SystemSnapshot.Clear();
            foreach (var entry in wanted) SystemSnapshot.Add(entry);
        }

        // a category that only ever produces one tile takes every group LHM filed under it; LibreHardwareMonitor
        // splits memory across several groups, so anything else would report a fraction of the real count
        //
        // the categories that produce one tile per device pick a single group each, and a group is consumed once
        // it has been claimed, so two GPUs never both show the sensors of the one group LHM has found so far
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

        // pulled rather than bound, so the page can call it both when UpdateService answers and when the theme
        // changes; the badge brush is a plain brush and would otherwise keep a stale accent
        public void RefreshUpdateState()
        {
            var service = UpdateService.Instance;
            var release = service.LatestRelease;

            // before the first successful check there is no release to name, so the button offers the notes of the
            // version that is actually running
            string notesVersion = release != null && !string.IsNullOrEmpty(release.Version)
                ? release.Version
                : UpdateService.CurrentVersion;
            ReleaseNotesSubtitle = $"Release notes for {UpdateService.VersionLabel(notesVersion)}";

            IsUpdateChecking = service.UiState == UpdateUiState.Checking;
            IsUpdateActionEnabled = service.UiState != UpdateUiState.StoreManaged
                && service.UiState != UpdateUiState.Checking;

            string lastChecked = service.LastCheckedAt.HasValue
                ? $"Last checked {service.LastCheckedAt.Value:dd.MM. HH:mm}"
                : "Not checked yet";

            switch (service.UiState)
            {
                case UpdateUiState.UpToDate:
                    SetBadge("", SuccessColor);
                    UpdateStatusTitle = "You are up to date";
                    UpdateStatusDescription = lastChecked;
                    break;

                case UpdateUiState.Checking:
                    SetBadge("", AccentColor());
                    UpdateStatusTitle = "Checking for updates";
                    UpdateStatusDescription = "";
                    break;

                case UpdateUiState.UpdateAvailable:
                    SetBadge("", AccentColor());
                    UpdateStatusTitle = "Update available";
                    UpdateStatusDescription = $"{UpdateService.VersionLabel(service.Latest?.Version)} is ready to install";
                    break;

                case UpdateUiState.Skipped:
                    SetBadge("", CautionColor);
                    UpdateStatusTitle = $"{UpdateService.VersionLabel(service.SkippedVersion)} skipped";
                    UpdateStatusDescription = "Select to install it anyway";
                    break;

                case UpdateUiState.Failed:
                    SetBadge("", CriticalColor);
                    UpdateStatusTitle = "Check failed";
                    UpdateStatusDescription = "Could not reach GitHub, select to try again";
                    break;

                case UpdateUiState.StoreManaged:
                    SetBadge("", NeutralColor);
                    UpdateStatusTitle = "Managed by Microsoft Store";
                    UpdateStatusDescription = "Updates are delivered through the Store";
                    break;

                default:
                    SetBadge("", NeutralColor);
                    UpdateStatusTitle = "Check for updates";
                    UpdateStatusDescription = lastChecked;
                    break;
            }
        }

        private void SetBadge(string glyph, Color color)
        {
            UpdateBadgeGlyph = glyph;
            UpdateBadgeBrush = new SolidColorBrush(color);
        }

        // the users Windows accent, which is independent of light/dark; falls back to the WinUI default accent
        private static Color AccentColor() =>
            Application.Current.Resources.TryGetValue("SystemAccentColor", out object value) && value is Color color
                ? color
                : AccentFallbackColor;


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
        // than rendered, a snapshot tile should not show a bare dash where a value belongs
        private static SystemSnapshotEntry Row(HardwareGroupKind kind, string title, params string[] details) =>
            LabelledRow(kind, HardwareGroupInfo.GetProfile(kind).Label, title, details);

        // deliberately a different name rather than an overload of Row: an overload taking one more string would
        // also match every Row(kind, title, detail, detail) call, and C# prefers the form that does not have to
        // expand params, so the device name would silently land in the category and the first fact in the title
        private static SystemSnapshotEntry LabelledRow(HardwareGroupKind kind, string category, string title, params string[] details)
        {
            var profile = HardwareGroupInfo.GetProfile(kind);

            return new SystemSnapshotEntry(
                profile.IconGlyph,
                IconBrushFor(kind),
                category,
                string.IsNullOrWhiteSpace(title) ? "Unknown" : title,
                details.Where(d => !string.IsNullOrWhiteSpace(d) && d != "-").ToList(),
                kind,
                title ?? "");
        }


        // tinted per category only while group colouring is on, otherwise the ordinary foreground, which is
        // what makes the icon read as a plain white glyph in the dark theme
        private static SolidColorBrush IconBrushFor(HardwareGroupKind kind)
        {
            if (HardwareColorMode.UseGroupColors)
            {
                return new SolidColorBrush(HardwareGroupInfo.GetProfile(kind).Color);
            }

            return Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out object value)
                && value is SolidColorBrush brush
                    ? brush
                    : new SolidColorBrush(Microsoft.UI.Colors.White);
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

            rows.Add(LabelledRow(HardwareGroupKind.Other, "Motherboard", name, bios, board.BiosReleaseDate));
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
