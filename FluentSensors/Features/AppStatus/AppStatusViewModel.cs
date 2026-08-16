using Microsoft.UI.Dispatching;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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

        // below this TitleBar width the Windows group hides and only the LHM group stays visible
        private const double MinWidthForFullStatus = 700;

        // the three inputs behind IsLhmGroupVisible/IsWindowsGroupVisible below; see UpdateVisibility
        private bool _isAppReady;
        private bool _isStatusEnabled;
        private bool _hasEnoughWidthForFull = true;

        private bool _isLhmGroupVisible;
        private bool _isWindowsGroupVisible;

        private bool _isDotNetRuntimeMissing;


        // === constructor ===

        public AppStatusViewModel()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _isStatusEnabled = SettingsService.Instance.StatusReadoutEnabled;
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

        // toggled by a plain Button click in the title bar (StatusToggleButton_Click in MainWindow), persists
        // across restarts via SettingsService
        public bool IsStatusEnabled
        {
            get => _isStatusEnabled;
            set
            {
                if (_isStatusEnabled == value) return;
                _isStatusEnabled = value;
                SettingsService.Instance.StatusReadoutEnabled = value;
                UpdateVisibility();
            }
        }

        // set from MainWindows TitleBar SizeChanged, see UpdateAvailableWidth
        public bool HasEnoughWidthForFull
        {
            get => _hasEnoughWidthForFull;
            set { if (_hasEnoughWidthForFull == value) return; _hasEnoughWidthForFull = value; UpdateVisibility(); }
        }

        // lhm group has priority: visible whenever the app is ready and the toggle is on, regardless of width
        public bool IsLhmGroupVisible
        {
            get => _isLhmGroupVisible;
            private set { _isLhmGroupVisible = value; OnPropertyChanged(); }
        }

        // windows group additionally needs enough room, this is the one that gives way first on a narrow window
        public bool IsWindowsGroupVisible
        {
            get => _isWindowsGroupVisible;
            private set { _isWindowsGroupVisible = value; OnPropertyChanged(); }
        }

        // set once from MainWindow right next to IsAppReady, after WinStaticInfoServices one-time registry check
        // has resolved; stays false (hint hidden) until then, independent of IsStatusEnabled since this hint
        // should not be hideable by the same toggle that hides the CPU/RAM readout
        public bool IsDotNetRuntimeMissing
        {
            get => _isDotNetRuntimeMissing;
            set { if (_isDotNetRuntimeMissing == value) return; _isDotNetRuntimeMissing = value; OnPropertyChanged(); }
        }


        // === public api ===

        // called from MainWindows TitleBar SizeChanged handler
        public void UpdateAvailableWidth(double titleBarWidth)
        {
            HasEnoughWidthForFull = titleBarWidth >= MinWidthForFullStatus;
        }


        // === private helpers ===

        // recomputes both group visibilities from the three inputs above, called whenever any of them changes
        private void UpdateVisibility()
        {
            IsLhmGroupVisible = IsAppReady && IsStatusEnabled;
            IsWindowsGroupVisible = IsAppReady && IsStatusEnabled && HasEnoughWidthForFull;
        }

        // AppStatusService already fires this from the UI thread now (see its own Tick()), TryEnqueue here is just
        // a defensive no-op safety net in case that ever changes
        private void OnStatusUpdated(AppStatusData data)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                SensorsText = $"Sensors: {data.SensorsFound}/{data.SensorsRendered}";
                PollText = $"Poll: {data.ActualUpdateIntervalMs:0}/{data.AimedUpdateIntervalMs}ms";
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
