using Microsoft.UI.Dispatching;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Common.UI;
using FluentSensors.Core;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.AppStatus
{
    // formats the raw AppStatusService numbers into display strings for the title bar readout
    // the future App Status page reuses the same AppStatusService, just with a fuller view model on top
    public class AppStatusViewModel : INotifyPropertyChanged
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;

        private string _sensorsText = "";
        private string _pollText = "";
        private string _cpuUsageText = "";
        private string _ramUsageText = "";
        private string _handleCountText = "";
        private string _gcMemoryText = "";

        // below this TitleBar width only one group fits, so the group placed second hides
        private const double MinWidthForFullStatus = 750;

        // the inputs behind IsLhmGroupVisible/IsWindowsGroupVisible below; everything except _isAppReady and
        // _hasEnoughWidthForFull mirrors a persisted setting, see UpdateVisibility
        private bool _isAppReady;
        private bool _isStatusEnabled;
        private bool _isStatusCollapsed;
        private bool _hasEnoughWidthForFull = true;
        private bool _isLhmGroupEnabled = true;
        private bool _isWindowsGroupEnabled = true;
        private bool _isLhmGroupFirst = true;

        private bool _isLhmGroupVisible;
        private bool _isWindowsGroupVisible;
        private bool _isStatusToggleVisible = true;

        private bool _isDotNetRuntimeMissing;

        // the update pill sits in the same title bar as the readout, so its state belongs to this view model even
        // though it has nothing to do with the status groups; both are set from MainWindow once UpdateService answers
        private bool _isUpdateAvailable;
        private string _updateVersionText = "";


        // === constructor ===

        public AppStatusViewModel()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            ReadStatusSettings();

            // both groups stay hidden until IsAppReady flips anyway, this is for the toggle button, which is already
            // on screen during the splash and would otherwise appear and disappear again
            UpdateVisibility();

            AppStatusService.Instance.StatusUpdated += OnStatusUpdated;
        }


        // === bindable properties ===

        // found/rendered, e.g. "Sensors: 169/3"
        public string SensorsText
        {
            get => _sensorsText;
            private set { _sensorsText = value; OnPropertyChanged(); }
        }

        // actual/aimed polling interval, e.g. "Poll: 289/100ms"
        public string PollText
        {
            get => _pollText;
            private set { _pollText = value; OnPropertyChanged(); }
        }

        public string CpuUsageText
        {
            get => _cpuUsageText;
            private set { _cpuUsageText = value; OnPropertyChanged(); }
        }

        public string RamUsageText
        {
            get => _ramUsageText;
            private set { _ramUsageText = value; OnPropertyChanged(); }
        }

        public string HandleCountText
        {
            get => _handleCountText;
            private set { _handleCountText = value; OnPropertyChanged(); }
        }

        public string GcMemoryText
        {
            get => _gcMemoryText;
            private set { _gcMemoryText = value; OnPropertyChanged(); }
        }

        // true once the splash screen is gone; both groups stay hidden before that regardless of the other two
        // inputs, set from MainWindow right where SplashOverlay gets collapsed
        public bool IsAppReady
        {
            get => _isAppReady;
            set { if (_isAppReady == value) return; _isAppReady = value; UpdateVisibility(); }
        }

        // master on/off, owned by the settings page alone; off takes the title bar toggle button with it, so the
        // readout cannot be brought back from up there
        public bool IsStatusEnabled => _isStatusEnabled;

        // what the title bar toggle button flips (StatusToggleButton_Click in MainWindow): a plain hide on top of
        // the master switch above, persisted so the readout stays hidden across restarts
        public bool IsStatusCollapsed
        {
            get => _isStatusCollapsed;
            set
            {
                if (_isStatusCollapsed == value) return;
                _isStatusCollapsed = value;
                SettingsService.Instance.StatusReadoutCollapsed = value;
                UpdateVisibility();
            }
        }

        // set from MainWindows TitleBar SizeChanged, see UpdateAvailableWidth
        public bool HasEnoughWidthForFull
        {
            get => _hasEnoughWidthForFull;
            set { if (_hasEnoughWidthForFull == value) return; _hasEnoughWidthForFull = value; UpdateVisibility(); }
        }

        // whether each group is wanted at all; the settings page is the only writer, these just mirror it back into
        // the visibility logic
        public bool IsLhmGroupEnabled => _isLhmGroupEnabled;
        public bool IsWindowsGroupEnabled => _isWindowsGroupEnabled;

        // which group sits in the leading title bar column; MainWindow moves the two groups accordingly
        public bool IsLhmGroupFirst => _isLhmGroupFirst;

        // visible whenever the app is ready, the readout is on and not collapsed, the group is wanted, and it either
        // leads or there is still room for the trailing one
        public bool IsLhmGroupVisible
        {
            get => _isLhmGroupVisible;
            private set { _isLhmGroupVisible = value; OnPropertyChanged(); }
        }

        // same rules as the lhm group above, just from the other side of the order
        public bool IsWindowsGroupVisible
        {
            get => _isWindowsGroupVisible;
            private set { _isWindowsGroupVisible = value; OnPropertyChanged(); }
        }

        // the title bar toggle button is gone once the readout is off in the settings, and equally once neither
        // group is left for it to show
        public bool IsStatusToggleVisible
        {
            get => _isStatusToggleVisible;
            private set { if (_isStatusToggleVisible == value) return; _isStatusToggleVisible = value; OnPropertyChanged(); }
        }

        // set once from MainWindow right next to IsAppReady, after WinStaticInfoServices one-time registry check
        // has resolved; stays false (hint hidden) until then, independent of IsStatusEnabled since this hint
        // should not be hideable by the same toggle that hides the CPU/RAM readout
        public bool IsDotNetRuntimeMissing
        {
            get => _isDotNetRuntimeMissing;
            set { if (_isDotNetRuntimeMissing == value) return; _isDotNetRuntimeMissing = value; OnPropertyChanged(); }
        }

        // stays false for the whole session unless a newer GitHub release is found; a store build never checks,
        // so the pill is simply never shown there
        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set { if (_isUpdateAvailable == value) return; _isUpdateAvailable = value; OnPropertyChanged(); }
        }

        // the bare version on the pill, e.g. "1.3.0"
        public string UpdateVersionText
        {
            get => _updateVersionText;
            set { if (_updateVersionText == value) return; _updateVersionText = value; OnPropertyChanged(); }
        }


        // === public api ===

        // called from MainWindows TitleBar SizeChanged handler
        public void UpdateAvailableWidth(double titleBarWidth)
        {
            HasEnoughWidthForFull = titleBarWidth >= MinWidthForFullStatus;
        }

        // re-reads the five readout settings after one of them changed
        // MainWindow owns that subscription, since a changed order also has to be applied to the actual title bar
        // columns in the same step
        public void RefreshStatusSettings()
        {
            ReadStatusSettings();
            UpdateVisibility();
        }


        // === private helpers ===

        private void ReadStatusSettings()
        {
            _isStatusEnabled = SettingsService.Instance.StatusReadoutEnabled;
            _isStatusCollapsed = SettingsService.Instance.StatusReadoutCollapsed;
            _isLhmGroupEnabled = SettingsService.Instance.StatusLhmGroupEnabled;
            _isWindowsGroupEnabled = SettingsService.Instance.StatusWindowsGroupEnabled;
            _isLhmGroupFirst = SettingsService.Instance.StatusGroupOrder == StatusGroupOrder.LhmFirst;
        }

        // recomputes both group visibilities from the inputs above, called whenever any of them changes
        private void UpdateVisibility()
        {
            bool readoutOn = IsAppReady && IsStatusEnabled && !IsStatusCollapsed;

            // only the group placed second gives way on a narrow window; with a single group switched on there is
            // nothing it would have to make room for, so it stays whatever the width is
            bool trailingFits = !(_isLhmGroupEnabled && _isWindowsGroupEnabled) || HasEnoughWidthForFull;

            IsLhmGroupVisible = readoutOn && _isLhmGroupEnabled && (_isLhmGroupFirst || trailingFits);
            IsWindowsGroupVisible = readoutOn && _isWindowsGroupEnabled && (!_isLhmGroupFirst || trailingFits);
            IsStatusToggleVisible = _isStatusEnabled && (_isLhmGroupEnabled || _isWindowsGroupEnabled);
        }

        // AppStatusService already fires this from the UI thread now (see its own Tick()), TryEnqueue here is just
        // a defensive no-op safety net in case that ever changes
        private void OnStatusUpdated(AppStatusData data)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                SensorsText = $"Sensors: {data.SensorsFound}/{data.SensorsRendered}";
                // measured cadence over the configured one, plus what a full read costs
                // the polling loop holds the configured interval as a floor, so the measured value sits just above it
                // while LHM keeps up; the read duration is what says how much headroom is left before it stops
                PollText = $"Poll: {data.ActualUpdateIntervalMs:0}/{data.AimedUpdateIntervalMs}ms (read {data.ReadDurationMs:0}ms)";
                CpuUsageText = $"CPU: {data.CpuUsagePercent:0.0}%";
                RamUsageText = $"RAM: {data.RamUsageBytes / 1024.0 / 1024.0:0} MB";
                HandleCountText = $"Handles: {data.HandleCount}";
                GcMemoryText = $"GC: {data.GcMemoryBytes / 1024.0 / 1024.0:0.0} MB";
            });
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
