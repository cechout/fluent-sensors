using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using FluentSensors.Common.Localization;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Common.Sensors;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Performance.Lhm
{
    // one cpu:
    // one per detected CPU (more only on a multi-socket system); data plus the matching of its own cores,
    // LhmCpuPerformanceViewModel does the discovery
    public class LhmCpuInstanceViewModel : INotifyPropertyChanged
    {
        // === fields ===

        // physical cores by their Load core number ("3" from "CPU Core #3 Thread #1"), so a
        // second thread reuses its core
        private readonly Dictionary<string, LhmCpuCoreViewModel> _coresByLoadIndex = new();

        // every physical core in Load discovery order, the sequence Temperature and Clock matching walks (see
        // MatchNextTemperature); (CoresWithThreads and CoresWithoutThreads are the UI split and lose that order)
        private readonly List<LhmCpuCoreViewModel> _coresInDiscoveryOrder = new();
        private int _nextTemperatureMatchIndex;
        private int _nextClockMatchIndex;


        // === constructor ===

        public LhmCpuInstanceViewModel(string hardwareName)
        {
            HardwareName = hardwareName;
            CoresWithThreads = new ObservableCollection<LhmCpuCoreViewModel>();
            CoresWithoutThreads = new ObservableCollection<LhmCpuCoreViewModel>();

            // switch candidates of the three overview categories, filled by LhmCpuPerformanceViewModel
            TotalLoadOptions = new ObservableCollection<SensorSwitchCandidate>();
            MaxTemperatureOptions = new ObservableCollection<SensorSwitchCandidate>();
            PackagePowerOptions = new ObservableCollection<SensorSwitchCandidate>();

            // computed per-group averages for the All Threads tiles; the id is only unique per CPU instance
            // (identically named sockets are not a case seen)
            AvgLoadWithThreads = new SensorGraphViewModel($"{hardwareName}-avg-load-with-threads", AppStrings.Get("Cpu_AverageLoad"), "Load");
            AvgTemperatureWithThreads = new SensorGraphViewModel($"{hardwareName}-avg-temperature-with-threads", AppStrings.Get("Cpu_AverageTemperature"), "Temperature");
            AvgClockWithThreads = new SensorGraphViewModel($"{hardwareName}-avg-clock-with-threads", AppStrings.Get("Cpu_AverageClock"), "Clock");
            AvgLoadWithoutThreads = new SensorGraphViewModel($"{hardwareName}-avg-load-without-threads", AppStrings.Get("Cpu_AverageLoad"), "Load");
            AvgTemperatureWithoutThreads = new SensorGraphViewModel($"{hardwareName}-avg-temperature-without-threads", AppStrings.Get("Cpu_AverageTemperature"), "Temperature");
            AvgClockWithoutThreads = new SensorGraphViewModel($"{hardwareName}-avg-clock-without-threads", AppStrings.Get("Cpu_AverageClock"), "Clock");
        }


        // === bindable properties ===

        public string HardwareName { get; }

        // the public setter persists the choice, SetXWithoutPersisting is for the default or restored graph at
        // discovery; MaxTemperature and PackagePower alike
        private SensorGraphViewModel _totalLoad;
        public SensorGraphViewModel TotalLoad
        {
            get => _totalLoad;
            set
            {
                if (_totalLoad == value) return;
                _totalLoad = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "Load", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> TotalLoadOptions { get; }

        internal void SetTotalLoadWithoutPersisting(SensorGraphViewModel value)
        {
            _totalLoad = value;
            OnPropertyChanged(nameof(TotalLoad));
        }

        private SensorGraphViewModel _maxTemperature;
        public SensorGraphViewModel MaxTemperature
        {
            get => _maxTemperature;
            set
            {
                if (_maxTemperature == value) return;
                _maxTemperature = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "Temperature", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> MaxTemperatureOptions { get; }

        internal void SetMaxTemperatureWithoutPersisting(SensorGraphViewModel value)
        {
            _maxTemperature = value;
            OnPropertyChanged(nameof(MaxTemperature));
        }

        private SensorGraphViewModel _packagePower;
        public SensorGraphViewModel PackagePower
        {
            get => _packagePower;
            set
            {
                if (_packagePower == value) return;
                _packagePower = value;
                OnPropertyChanged();
                if (value != null) SensorSwitchStateService.Instance.SetSelectedSensorId(HardwareName, "Power", value.SensorId);
            }
        }
        public ObservableCollection<SensorSwitchCandidate> PackagePowerOptions { get; }

        internal void SetPackagePowerWithoutPersisting(SensorGraphViewModel value)
        {
            _packagePower = value;
            OnPropertyChanged(nameof(PackagePower));
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

        // physical cores whose Load sensor has a "Thread #" suffix, more than one logical processor; (a
        // name-based fact, unlike a P/E label)
        public ObservableCollection<LhmCpuCoreViewModel> CoresWithThreads { get; }

        // without the suffix
        public ObservableCollection<LhmCpuCoreViewModel> CoresWithoutThreads { get; }

        // a group with at least one core shows its section
        public bool HasCoresWithThreads => CoresWithThreads.Count > 0;
        public bool HasCoresWithoutThreads => CoresWithoutThreads.Count > 0;

        // the group titles only once there is a split to label
        public bool ShowCoreGroupTitles => HasCoresWithThreads && HasCoresWithoutThreads;

        // All Threads tiles; average load, temperature and clock per core group
        public SensorGraphViewModel AvgLoadWithThreads { get; }
        public SensorGraphViewModel AvgTemperatureWithThreads { get; }
        public SensorGraphViewModel AvgClockWithThreads { get; }
        public SensorGraphViewModel AvgLoadWithoutThreads { get; }
        public SensorGraphViewModel AvgTemperatureWithoutThreads { get; }
        public SensorGraphViewModel AvgClockWithoutThreads { get; }

        // --- workaround: SensorGraphControl permanently blank after Collapsed + Unload/Reload ---
        // problem and fix: see GpuDetailView.SetLayoutActive; the Overall/All Threads switch hides by
        // Opacity and IsHitTestVisible too
        public double OverallOpacity => IsShowingAllThreads ? 0 : 1;
        public bool OverallIsHitTestVisible => !IsShowingAllThreads;

        public double AllThreadsOpacity => IsShowingAllThreads ? 1 : 0;
        public bool AllThreadsIsHitTestVisible => IsShowingAllThreads;

        // static CPU info; computed, WinStaticInfoService never changes, so no notifications
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

        // the physical core of a Load core number, created and grouped the first time; a second thread reuses it
        public LhmCpuCoreViewModel GetOrCreateCore(string loadCoreNumber, bool hasThreads)
        {
            if (_coresByLoadIndex.TryGetValue(loadCoreNumber, out var existing)) return existing;

            var core = new LhmCpuCoreViewModel(hasThreads);
            _coresByLoadIndex[loadCoreNumber] = core;
            _coresInDiscoveryOrder.Add(core);
            (hasThreads ? CoresWithThreads : CoresWithoutThreads).Add(core);

            // group membership changed; its section and the title split re-evaluate
            OnPropertyChanged(hasThreads ? nameof(HasCoresWithThreads) : nameof(HasCoresWithoutThreads));
            OnPropertyChanged(nameof(ShowCoreGroupTitles));

            return core;
        }

        // gives the next unmatched core (Load order) this Temperature graph and label and returns it for the group
        // average; relies on LHM reporting temperatures in the Load order
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

        // one core group Load average from every thread, pushed as a data point; once per thread tick
        // (Temperature and Clock alike)
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
        private static string FormatBool(bool value) => HardwareInfoFormatter.FormatYesNo(value);

        // hybrid labels rest on the documented EfficiencyClass: "a core with a higher value for the efficiency
        // class has intrinsically greater performance and less efficiency"; so the highest group is
        // Performance, the lowest Efficient
        // https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-processor_relationship
        private static string FormatCoreTopology(WinCpuInfo cpu)
        {
            if (cpu.CoreTopology.Count == 0) return "-";

            var groups = cpu.CoreTopology
                .GroupBy(c => c.EfficiencyClass)
                .OrderByDescending(g => g.Key)
                .ToList();

            // one group is a non-hybrid CPU (EfficiencyClass is only nonzero on a P/E set);
            // SMT is all this line can add
            if (groups.Count == 1)
            {
                bool hasSmt = cpu.CoreTopology.Any(c => c.HasSmt);
                return AppStrings.Get(hasSmt ? "Cpu_TopologySymmetricSmt" : "Cpu_TopologySymmetric");
            }

            var parts = new List<string>();
            for (int i = 0; i < groups.Count; i++)
            {
                // only the extremes have a confirmed meaning; a class in between (not seen yet) gets
                // a number, not a guessed name
                string label = i == 0 ? AppStrings.Get("Cpu_TopologyPerformance")
                    : i == groups.Count - 1 ? AppStrings.Get("Cpu_TopologyEfficient")
                    : AppStrings.Format("Cpu_TopologyClass", groups[i].Key);

                int coreCount = groups[i].Count();
                int threadCount = groups[i].Sum(c => c.LogicalProcessorIndices.Count);
                parts.Add(AppStrings.Format("Cpu_TopologyGroup", coreCount, label, AppStrings.Plural("Start_Threads", threadCount)));
            }

            return string.Join(" + ", parts);
        }

        // averages the latest values into a data point of the target; unmatched graphs (null)
        // are skipped, not counted as 0
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
