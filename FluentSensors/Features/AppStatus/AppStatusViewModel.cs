using Microsoft.UI.Dispatching;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Common.UI;
using FluentSensors.Core;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.AppStatus
{
    // the title bar readout:
    // the AppStatusService numbers as display strings; (an App Status page would reuse the
    // service with a fuller view model)
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

        // below it the second group hides; the title bar width less what sits in front, see UpdateAvailableWidth
        private const double MinWidthForFullStatus = 750;

        // the inputs of the group visibility (see UpdateVisibility); all but _isAppReady and
        // _hasEnoughWidthForFull mirror a setting
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
        private bool _isPawnIoMissing;

        // the update pill shares the title bar, so its state lives here too; set by
        // MainWindow on an UpdateService answer
        private bool _isUpdateAvailable;
        private string _updateVersionText = "";


        // === constructor ===

        public AppStatusViewModel()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            ReadStatusSettings();

            // for the toggle button, already on screen during the splash (the groups wait for IsAppReady)
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

        // actual/aimed interval and the read, e.g. "Poll: 289/100ms (read 40ms)"
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

        // the splash is gone (set by MainWindow); the groups stay hidden before that
        public bool IsAppReady
        {
            get => _isAppReady;
            set { if (_isAppReady == value) return; _isAppReady = value; UpdateVisibility(); }
        }

        // master switch, settings page only; off takes the title bar toggle with it
        public bool IsStatusEnabled => _isStatusEnabled;

        // the title bar toggle (StatusToggleButton_Click); a persisted hide on top of the master switch
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

        // see UpdateAvailableWidth
        public bool HasEnoughWidthForFull
        {
            get => _hasEnoughWidthForFull;
            set { if (_hasEnoughWidthForFull == value) return; _hasEnoughWidthForFull = value; UpdateVisibility(); }
        }

        // whether each group is wanted; the settings page writes them
        public bool IsLhmGroupEnabled => _isLhmGroupEnabled;
        public bool IsWindowsGroupEnabled => _isWindowsGroupEnabled;

        // the leading group; MainWindow moves the columns
        public bool IsLhmGroupFirst => _isLhmGroupFirst;

        // app ready, readout on and not collapsed, group wanted, and leading or with room for the trailing one
        public bool IsLhmGroupVisible
        {
            get => _isLhmGroupVisible;
            private set { _isLhmGroupVisible = value; OnPropertyChanged(); }
        }

        // the same rules from the other side of the order
        public bool IsWindowsGroupVisible
        {
            get => _isWindowsGroupVisible;
            private set { _isWindowsGroupVisible = value; OnPropertyChanged(); }
        }

        // gone with the readout off, or with no group left to show
        public bool IsStatusToggleVisible
        {
            get => _isStatusToggleVisible;
            private set { if (_isStatusToggleVisible == value) return; _isStatusToggleVisible = value; OnPropertyChanged(); }
        }

        // set once with IsAppReady, after the WinStaticInfoService runtime check; independent of IsStatusEnabled, the
        // readout toggle must not hide this hint
        public bool IsDotNetRuntimeMissing
        {
            get => _isDotNetRuntimeMissing;
            set { if (_isDotNetRuntimeMissing == value) return; _isDotNetRuntimeMissing = value; OnPropertyChanged(); }
        }

        public bool IsPawnIoMissing
        {
            get => _isPawnIoMissing;
            set { if (_isPawnIoMissing == value) return; _isPawnIoMissing = value; OnPropertyChanged(); }
        }

        // while UpdateService reports an update (GitHub, or the store in the store build)
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

        // from MainWindow; reservedWidth is what sits in front of the readout (update pill, prerequisite hints), so
        // MinWidthForFullStatus is about the readout alone
        public void UpdateAvailableWidth(double titleBarWidth, double reservedWidth)
        {
            HasEnoughWidthForFull = titleBarWidth - reservedWidth >= MinWidthForFullStatus;
        }

        // re-reads the five readout settings; (MainWindow owns the subscription, a new order moves its columns too)
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

        // both group visibilities, on any input change
        private void UpdateVisibility()
        {
            bool readoutOn = IsAppReady && IsStatusEnabled && !IsStatusCollapsed;

            // only the second group gives way; a single group stays whatever the width
            bool trailingFits = !(_isLhmGroupEnabled && _isWindowsGroupEnabled) || HasEnoughWidthForFull;

            IsLhmGroupVisible = readoutOn && _isLhmGroupEnabled && (_isLhmGroupFirst || trailingFits);
            IsWindowsGroupVisible = readoutOn && _isWindowsGroupEnabled && (!_isLhmGroupFirst || trailingFits);
            IsStatusToggleVisible = _isStatusEnabled && (_isLhmGroupEnabled || _isWindowsGroupEnabled);
        }

        // AppStatusService raises this on the UI thread already (see Tick()); TryEnqueue is a safety net
        private void OnStatusUpdated(AppStatusData data)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                SensorsText = $"Sensors: {data.SensorsFound}/{data.SensorsRendered}";
                // measured over configured cadence, plus the read; (the read says how much headroom is left)
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
