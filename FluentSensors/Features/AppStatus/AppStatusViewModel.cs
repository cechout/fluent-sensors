using Microsoft.UI.Dispatching;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using FluentSensors.Core;


namespace FluentSensors.Features.AppStatus
{
    // formats the raw AppStatusService numbers into display strings for the title bar readout
    // the future App Status page reuses the same AppStatusService, just with a fuller view model on top
    public class AppStatusViewModel : INotifyPropertyChanged
    {
        // === fields ===

        private readonly DispatcherQueue _dispatcherQueue;

        private string _sensorsFoundText = "";
        private string _sensorsRenderedText = "";
        private string _cpuUsageText = "";
        private string _ramUsageText = "";
        private string _handleCountText = "";
        private string _gcMemoryText = "";

        // below this TitleBar width the Windows group hides and only the LHM group stays visible; adjust to change
        // where that cutoff sits
        private const double MinWidthForFullStatus = 900;

        // the three inputs behind IsLhmGroupVisible/IsWindowsGroupVisible below; see UpdateVisibility
        private bool _isAppReady;
        private bool? _isStatusEnabled = true;
        private bool _hasEnoughWidthForFull = true;

        private bool _isLhmGroupVisible;
        private bool _isWindowsGroupVisible;


        // === constructor ===

        public AppStatusViewModel()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            AppStatusService.Instance.StatusUpdated += OnStatusUpdated;
        }


        // === bindable properties ===

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

        // the manual toggle button next to the title, defaults on
        // bool? (not bool) to match ToggleButton.IsChecked exactly, so the TwoWay bind needs no converter
        // never actually null in practice, the button is a plain two-state toggle, not IsThreeState
        public bool? IsStatusEnabled
        {
            get => _isStatusEnabled;
            set { if (_isStatusEnabled == value) return; _isStatusEnabled = value; UpdateVisibility(); }
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
            bool statusEnabled = IsStatusEnabled == true; // null (indeterminate) never actually happens, treated as off

            IsLhmGroupVisible = IsAppReady && statusEnabled;
            IsWindowsGroupVisible = IsAppReady && statusEnabled && HasEnoughWidthForFull;
        }

        // AppStatusService fires this from its own background timer thread; every property write below needs to
        // land on the UI thread since the title bar binds to them directly
        private void OnStatusUpdated(AppStatusData data)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                SensorsFoundText = $"total: {data.SensorsFound}";
                SensorsRenderedText = $"rendered: {data.SensorsRendered}";
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
