using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Common.Sensors;


namespace FluentSensors.Features.Performance.Lhm
{
    // one entry per detected CPU (a multi-socket system shows more than one, though in practice this is
    // virtually always exactly one)
    // A plain data holder plus a bit of matching logic for its own cores; LhmCpuPerformanceViewModel does the actual
    // sensor discovery/parsing and decides what goes where
    public class LhmCpuInstanceViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // physical cores are looked up by their Load-sensor core number (e.g. "3" from "CPU Core #3 Thread
        // #1") so a second thread of an already-known core reuses the same LhmCpuCoreViewModel instead of
        // creating a duplicate
        private readonly Dictionary<string, LhmCpuCoreViewModel> _coresByLoadIndex = new();

        // every physical core in the exact order LHMs Load sensors introduced it
        // the single shared reference sequence Temperature/Clock matching walks through (see MatchNextTemperature/
        // MatchNextClock)
        // deliberately not the same object as CoresWithThreads/CoresWithoutThreads concatenated; those two exist
        // purely for the UIs vertical split, this preserves the one combined discovery order that the matching relies on
        private readonly List<LhmCpuCoreViewModel> _coresInDiscoveryOrder = new();
        private int _nextTemperatureMatchIndex;
        private int _nextClockMatchIndex;


        // === constructor ===

        public LhmCpuInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;
            CoresWithThreads = new ObservableCollection<LhmCpuCoreViewModel>();
            CoresWithoutThreads = new ObservableCollection<LhmCpuCoreViewModel>();

            // synthetic per-group averages for the All Threads tiles; not discovered from LHM like the graphs above,
            // computed locally
            // sensor id only needs to be unique within one CPU instance, fine since multi-socket systems with identical
            // hardware names are not a case this app has seen in practice
            AvgLoadWithThreads = new SensorGraphViewModel($"{hardwareName}-avg-load-with-threads", "Average Load", "Load");
            AvgTemperatureWithThreads = new SensorGraphViewModel($"{hardwareName}-avg-temperature-with-threads", "Average Temperature", "Temperature");
            AvgClockWithThreads = new SensorGraphViewModel($"{hardwareName}-avg-clock-with-threads", "Average Clock", "Clock");
            AvgLoadWithoutThreads = new SensorGraphViewModel($"{hardwareName}-avg-load-without-threads", "Average Load", "Load");
            AvgTemperatureWithoutThreads = new SensorGraphViewModel($"{hardwareName}-avg-temperature-without-threads", "Average Temperature", "Temperature");
            AvgClockWithoutThreads = new SensorGraphViewModel($"{hardwareName}-avg-clock-without-threads", "Average Clock", "Clock");
        }


        // === bindable properties ===

        public string HardwareName { get; }

        private SensorGraphViewModel _totalLoad;
        public SensorGraphViewModel TotalLoad
        {
            get => _totalLoad;
            set { _totalLoad = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _averageTemperature;
        public SensorGraphViewModel AverageTemperature
        {
            get => _averageTemperature;
            set { _averageTemperature = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _maxTemperature;
        public SensorGraphViewModel MaxTemperature
        {
            get => _maxTemperature;
            set { _maxTemperature = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _packagePower;
        public SensorGraphViewModel PackagePower
        {
            get => _packagePower;
            set { _packagePower = value; OnPropertyChanged(); }
        }

        private bool _isShowingAllThreads;
        public bool IsShowingAllThreads
        {
            get => _isShowingAllThreads;
            set
            {
                if (_isShowingAllThreads != value)
                {
                    _isShowingAllThreads = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(OverallOpacity));
                    OnPropertyChanged(nameof(OverallIsHitTestVisible));
                    OnPropertyChanged(nameof(AllThreadsOpacity));
                    OnPropertyChanged(nameof(AllThreadsIsHitTestVisible));
                }
            }
        }

        // physical cores whose Load sensor name contains a "Thread #" suffix (i.e. LHM reports more than one
        // logical processor for them); a safe, name-based fact, unlike any P-Core/E-Core label
        public ObservableCollection<LhmCpuCoreViewModel> CoresWithThreads { get; }

        // physical cores without a "Thread #" suffix in their Load sensor name
        public ObservableCollection<LhmCpuCoreViewModel> CoresWithoutThreads { get; }

        // all Threads tiles: average Load/Temperature/Clock per core group
        public SensorGraphViewModel AvgLoadWithThreads { get; }
        public SensorGraphViewModel AvgTemperatureWithThreads { get; }
        public SensorGraphViewModel AvgClockWithThreads { get; }
        public SensorGraphViewModel AvgLoadWithoutThreads { get; }
        public SensorGraphViewModel AvgTemperatureWithoutThreads { get; }
        public SensorGraphViewModel AvgClockWithoutThreads { get; }

        // --- workaround: SensorGraphControl permanently blank after Collapsed + Unload/Reload ---
        // problem/fix: see GpuDetailView.xaml.cs SetLayoutActive for the full explanation; the Overall/All-Threads
        // switch hits the exact same trap, so it gets the same Opacity+IsHitTestVisible treatment instead of a
        // real Visibility toggle
        public double OverallOpacity => IsShowingAllThreads ? 0 : 1;
        public bool OverallIsHitTestVisible => !IsShowingAllThreads;

        public double AllThreadsOpacity => IsShowingAllThreads ? 1 : 0;
        public bool AllThreadsIsHitTestVisible => IsShowingAllThreads;

        // static CPU info
        // read-only, purely computed; WinStaticInfoService.Instance.Cpu never changes after the singletons first
        // access, so there is nothing to raise OnPropertyChanged for here
        public string CpuPhysicalCoresText => WinStaticInfoService.Instance.Cpu.PhysicalCores.ToString();
        public string CpuLogicalProcessorsText => WinStaticInfoService.Instance.Cpu.LogicalProcessors.ToString();
        public string CpuL1CacheText => HardwareInfoFormatter.FormatCacheLevelTotal(WinStaticInfoService.Instance.Cpu.CacheEntries, level: 3);
        public string CpuL2CacheText => HardwareInfoFormatter.FormatCacheLevelTotal(WinStaticInfoService.Instance.Cpu.CacheEntries, level: 4);
        public string CpuL3CacheText => HardwareInfoFormatter.FormatCacheLevelTotal(WinStaticInfoService.Instance.Cpu.CacheEntries, level: 5);
        public string CpuMaxClockText => $"{WinStaticInfoService.Instance.Cpu.MaxClockSpeedMhz} MHz";
        public string CpuSocketText => WinStaticInfoService.Instance.Cpu.SocketDesignation;
        public string CpuVirtualizationFirmwareText => FormatBool(WinStaticInfoService.Instance.Cpu.VirtualizationFirmwareEnabled);
        public string CpuVirtualizationExtensionsText => FormatBool(WinStaticInfoService.Instance.Cpu.VirtualizationExtensionsSupported);
        public string CpuCoreTopologyText => FormatCoreTopology(WinStaticInfoService.Instance.Cpu);


        // === public methods ===

        // returns the physical core for this Load core number, creating it (and bucketing it into
        // CoresWithThreads/CoresWithoutThreads) the first time its seen; a second thread of an already-known core
        // reuses the same instance
        public LhmCpuCoreViewModel GetOrCreateCore(string loadCoreNumber, bool hasThreads)
        {
            if (_coresByLoadIndex.TryGetValue(loadCoreNumber, out var existing)) return existing;

            var core = new LhmCpuCoreViewModel(hasThreads);
            _coresByLoadIndex[loadCoreNumber] = core;
            _coresInDiscoveryOrder.Add(core);
            (hasThreads ? CoresWithThreads : CoresWithoutThreads).Add(core);
            return core;
        }

        // assigns the next unmatched core (in Load-discovery order) this Temperature graph + label, and returns that core
        // so the caller can trigger the right group average
        // relies entirely on LHM reporting Temperature sensors in the same per-core order Load already established
        public LhmCpuCoreViewModel MatchNextTemperature(SensorGraphViewModel graph, string label)
        {
            if (_nextTemperatureMatchIndex >= _coresInDiscoveryOrder.Count) return null;

            var core = _coresInDiscoveryOrder[_nextTemperatureMatchIndex];
            core.Temperature = graph;
            core.TemperatureLabel = label;
            _nextTemperatureMatchIndex++;
            return core;
        }

        public LhmCpuCoreViewModel MatchNextClock(SensorGraphViewModel graph, string label)
        {
            if (_nextClockMatchIndex >= _coresInDiscoveryOrder.Count) return null;

            var core = _coresInDiscoveryOrder[_nextClockMatchIndex];
            core.Clock = graph;
            core.ClockLabel = label;
            _nextClockMatchIndex++;
            return core;
        }

        // recomputes one core groups Load average from every threads latest value and pushes it as this averages new
        // data point; called once per thread tick, from LhmCpuPerformanceViewModel
        // Temperature/Clock below follow the exact same pattern, just averaging a different reading
        public void RecomputeLoadAverage(bool hasThreads)
        {
            var cores = hasThreads ? CoresWithThreads : CoresWithoutThreads;
            var target = hasThreads ? AvgLoadWithThreads : AvgLoadWithoutThreads;
            UpdateAverage(target, "Load", cores.SelectMany(c => c.Threads));
        }

        public void RecomputeTemperatureAverage(bool hasThreads)
        {
            var cores = hasThreads ? CoresWithThreads : CoresWithoutThreads;
            var target = hasThreads ? AvgTemperatureWithThreads : AvgTemperatureWithoutThreads;
            UpdateAverage(target, "Temperature", cores.Select(c => c.Temperature));
        }

        public void RecomputeClockAverage(bool hasThreads)
        {
            var cores = hasThreads ? CoresWithThreads : CoresWithoutThreads;
            var target = hasThreads ? AvgClockWithThreads : AvgClockWithoutThreads;
            UpdateAverage(target, "Clock", cores.Select(c => c.Clock));
        }


        // === private helpers ===

        private static string FormatCacheSize(int cacheSizeKb) => cacheSizeKb > 0 ? $"{cacheSizeKb} KB" : "-";
        private static string FormatBool(bool value) => value ? "Yes" : "No";

        // hybrid detection and labeling relies on the EfficiencyClass semantics documented for PROCESSOR_RELATIONSHIP:
        // "a core with a higher value for the efficiency class has intrinsically greater performance and less
        // efficiency than a core with a lower value" - confirmed by Microsoft, unlike the Win32_CacheMemory Level
        // numbering elsewhere in this app, so labeling the highest group "Performance" and the lowest "Efficient" is
        // safe without needing a second real-hardware cross-check
        // https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-processor_relationship
        private static string FormatCoreTopology(WinCpuInfo cpu)
        {
            if (cpu.CoreTopology.Count == 0) return "-";

            var groups = cpu.CoreTopology
                .GroupBy(c => c.EfficiencyClass)
                .OrderByDescending(g => g.Key)
                .ToList();

            // EfficiencyClass is only ever nonzero on systems with a heterogeneous (P/E) core set, per the docs above -
            // a single group here means a conventional, non-hybrid CPU; SMT status is the one detail this line can add
            // that Physical/Logical Cores above don't already cover
            if (groups.Count == 1)
            {
                bool hasSmt = cpu.CoreTopology.Any(c => c.HasSmt);
                return hasSmt ? "Symmetric, Hyper-Threading enabled" : "Symmetric, no Hyper-Threading";
            }

            var parts = new List<string>();
            for (int i = 0; i < groups.Count; i++)
            {
                // only the two extremes have a confirmed meaning (highest = most performant, lowest = most efficient);
                // a third class in between (not seen on real hardware yet) gets a plain numeric label instead of a
                // guessed name
                string label = i == 0 ? "Performance"
                    : i == groups.Count - 1 ? "Efficient"
                    : $"Class {groups[i].Key}";

                int coreCount = groups[i].Count();
                int threadCount = groups[i].Sum(c => c.LogicalProcessorIndices.Count);
                parts.Add($"{coreCount} {label} ({threadCount} threads)");
            }

            return string.Join(" + ", parts);
        }

        // averages the latest value of every given graph and pushes the result as the targets own new data point
        // graphs not matched yet (null, e.g. Temperature/Clock before LhmCpuPerformanceViewModel reaches this core) are
        // skipped rather than counted as 0, so an incomplete group does not drag its own average down
        private static void UpdateAverage(SensorGraphViewModel target, string sensorType, IEnumerable<SensorGraphViewModel> sourceGraphs)
        {
            var values = sourceGraphs.Where(g => g != null).Select(g => g.SensorData.LastOrDefault() ?? 0).ToList();
            if (values.Count == 0) return;

            double average = values.Average();
            target.AddDataPoint(average, SensorUnitFormatter.Format(average, sensorType));
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}