using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using FluentSensors.Controls.SensorGraph;
using FluentSensors.Core.StaticInfo;


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
                    OnPropertyChanged(nameof(OverallVisibility));
                    OnPropertyChanged(nameof(AllThreadsVisibility));
                }
            }
        }

        // physical cores whose Load sensor name contains a "Thread #" suffix (i.e. LHM reports more than one
        // logical processor for them); a safe, name-based fact, unlike any P-Core/E-Core label
        public ObservableCollection<LhmCpuCoreViewModel> CoresWithThreads { get; }

        // physical cores without a "Thread #" suffix in their Load sensor name
        public ObservableCollection<LhmCpuCoreViewModel> CoresWithoutThreads { get; }

        // pre-computed Visibility for the two CPU detail views; avoids function bindings inside the DataTemplate,
        // which x:Bind cannot reliably re-evaluate when a property buried inside the function body changes
        public Visibility OverallVisibility => IsShowingAllThreads ? Visibility.Collapsed : Visibility.Visible;
        public Visibility AllThreadsVisibility => IsShowingAllThreads ? Visibility.Visible : Visibility.Collapsed;

        // static CPU info
        // read-only, purely computed; WinStaticInfoService.Instance.Cpu never changes after the singletons first
        // access, so there is nothing to raise OnPropertyChanged for here
        public string CpuPhysicalCoresText => WinStaticInfoService.Instance.Cpu.PhysicalCores.ToString();
        public string CpuLogicalProcessorsText => WinStaticInfoService.Instance.Cpu.LogicalProcessors.ToString();
        public string CpuL2CacheText => FormatCacheSize(WinStaticInfoService.Instance.Cpu.L2CacheSizeKb);
        public string CpuL3CacheText => FormatCacheSize(WinStaticInfoService.Instance.Cpu.L3CacheSizeKb);
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

        // assigns the next unmatched core (in Load-discovery order) this Temperature graph + label
        // relies entirely on LHM reporting Temperature sensors in the same per-core order Load already established
        public void MatchNextTemperature(SensorGraphViewModel graph, string label)
        {
            if (_nextTemperatureMatchIndex >= _coresInDiscoveryOrder.Count) return;

            var core = _coresInDiscoveryOrder[_nextTemperatureMatchIndex];
            core.Temperature = graph;
            core.TemperatureLabel = label;
            _nextTemperatureMatchIndex++;
        }

        public void MatchNextClock(SensorGraphViewModel graph, string label)
        {
            if (_nextClockMatchIndex >= _coresInDiscoveryOrder.Count) return;

            var core = _coresInDiscoveryOrder[_nextClockMatchIndex];
            core.Clock = graph;
            core.ClockLabel = label;
            _nextClockMatchIndex++;
        }


        // === private helpers ===

        private static string FormatCacheSize(int cacheSizeKb) => cacheSizeKb > 0 ? $"{cacheSizeKb} KB" : "-";
        private static string FormatBool(bool value) => value ? "Yes" : "No";

        // deliberately does not label anything P-Core/E-Core (see WinCpuCoreTopologyEntry doc comment); just the raw
        // counts for now, real UI interpretation of EfficiencyClass is a later step
        private static string FormatCoreTopology(WinCpuInfo cpu)
        {
            int smtCores = cpu.CoreTopology.Count(c => c.HasSmt);
            int efficiencyClasses = cpu.CoreTopology.Select(c => c.EfficiencyClass).Distinct().Count();
            return $"{cpu.CoreTopology.Count} cores read, {smtCores} with SMT, {efficiencyClasses} efficiency class(es)";
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}