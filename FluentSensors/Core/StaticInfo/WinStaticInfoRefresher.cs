using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using WinUIEx.Messaging;


namespace FluentSensors.Core.StaticInfo
{
    // decides when WinStaticInfoService asks Windows again:
    // on an event where Windows has one (network change, device change, display change, the main window coming
    // back), on a slow timer for what has none (link speed, storage counters)
    // the prerequisites (PawnIO, .NET) only when the main window comes back
    // lives on the UI thread of the main window and only starts the queries; the service runs them off it
    public sealed class WinStaticInfoRefresher
    {
        // === fields ===

        private const uint WM_DISPLAYCHANGE = 0x007E;
        private const uint WM_DEVICECHANGE = 0x0219;
        private const ulong DBT_DEVNODES_CHANGED = 0x0007; // broadcast to every top level window, no registration

        // the timer; only while the main window is shown, nothing reads these values from the tray
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10); // network: link speed has no event
        // a WMI round trip to each drive; can reading the counters this often keep a sleeping hdd awake? unmeasured
        private static readonly TimeSpan DrivePollInterval = TimeSpan.FromSeconds(60);

        // the prerequisite look starts a process (dotnet --list-runtimes), so not on every activation in a row
        private static readonly TimeSpan PrerequisiteMinInterval = TimeSpan.FromSeconds(5);

        // one plug or network switch raises a burst of events; the query waits until they stop
        private static readonly TimeSpan NetworkSettleTime = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan DeviceSettleTime = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan DisplaySettleTime = TimeSpan.FromSeconds(1);

        private Window? _mainWindow;
        private DispatcherQueue? _dispatcherQueue;
        private WindowMessageMonitor? _messageMonitor; // must stay alive, it is the subclass of the main window

        private DispatcherQueueTimer? _pollTimer;
        private DispatcherQueueTimer? _networkSettleTimer;
        private DispatcherQueueTimer? _deviceSettleTimer;
        private DispatcherQueueTimer? _displaySettleTimer;

        // the running query per list; a trigger that lands while one runs is dropped
        private Task? _gpuRefresh;
        private Task? _driveRefresh;
        private Task? _networkRefresh;
        private Task? _prerequisiteRefresh;
        private long _driveRefreshTimestamp;
        private long _prerequisiteRefreshTimestamp;


        // === singleton instance ===

        private static readonly WinStaticInfoRefresher _instance = new WinStaticInfoRefresher();
        public static WinStaticInfoRefresher Instance => _instance;

        private WinStaticInfoRefresher() { }


        // === public api ===

        // once, on the UI thread, after the splash (the first query of the service is done by then)
        public void Start(Window mainWindow)
        {
            if (_mainWindow != null) return;

            _mainWindow = mainWindow;
            _dispatcherQueue = mainWindow.DispatcherQueue;
            _driveRefreshTimestamp = Stopwatch.GetTimestamp();

            _networkSettleTimer = CreateSettleTimer(NetworkSettleTime, RefreshNetworkAdapters);
            _displaySettleTimer = CreateSettleTimer(DisplaySettleTime, RefreshGpus);
            _deviceSettleTimer = CreateSettleTimer(DeviceSettleTime, () =>
            {
                RefreshGpus();
                RefreshDrives();
            });

            _pollTimer = _dispatcherQueue.CreateTimer();
            _pollTimer.Interval = PollInterval;
            _pollTimer.Tick += (s, e) => RefreshWhatIsDue();
            _pollTimer.Start();

            // raised on a thread pool thread
            NetworkChange.NetworkAddressChanged += (s, e) => _dispatcherQueue.TryEnqueue(() => Restart(_networkSettleTimer));
            NetworkChange.NetworkAvailabilityChanged += (s, e) => _dispatcherQueue.TryEnqueue(() => Restart(_networkSettleTimer));

            _messageMonitor = new WindowMessageMonitor(WinRT.Interop.WindowNative.GetWindowHandle(mainWindow));
            _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;

            // back from the tray or from another window; the timer stood still or the values are up to a tick old,
            // and it is the moment a user returns from installing what a hint asked for
            mainWindow.Activated += (s, e) =>
            {
                if (e.WindowActivationState == WindowActivationState.Deactivated) return;

                RefreshWhatIsDue();
                RefreshPrerequisites();
            };
        }


        // === event handlers ===

        // only looks, never handles; the messages go on to the window
        private void OnWindowMessageReceived(object? sender, WindowMessageEventArgs e)
        {
            if (e.Message.MessageId == WM_DEVICECHANGE && (ulong)e.Message.WParam == DBT_DEVNODES_CHANGED)
            {
                Restart(_deviceSettleTimer);
            }
            else if (e.Message.MessageId == WM_DISPLAYCHANGE)
            {
                Restart(_displaySettleTimer);
            }
        }


        // === private helpers ===

        // the network every time, the drives once their interval has passed
        private void RefreshWhatIsDue()
        {
            if (_mainWindow?.AppWindow.IsVisible != true) return;

            RefreshNetworkAdapters();

            if (Stopwatch.GetElapsedTime(_driveRefreshTimestamp) >= DrivePollInterval) RefreshDrives();
        }

        private void RefreshGpus()
        {
            if (_gpuRefresh is { IsCompleted: false }) return;
            _gpuRefresh = WinStaticInfoService.Instance.RefreshGpusAsync();
        }

        private void RefreshDrives()
        {
            if (_driveRefresh is { IsCompleted: false }) return;
            _driveRefreshTimestamp = Stopwatch.GetTimestamp();
            _driveRefresh = WinStaticInfoService.Instance.RefreshDrivesAsync();
        }

        private void RefreshNetworkAdapters()
        {
            if (_networkRefresh is { IsCompleted: false }) return;
            _networkRefresh = WinStaticInfoService.Instance.RefreshNetworkAdaptersAsync();
        }

        // only while one is missing; nothing to find otherwise
        private void RefreshPrerequisites()
        {
            var info = WinStaticInfoService.Instance;
            if (info.PawnIoState != WinPrerequisiteState.Missing && info.DotNetRuntimeState != WinPrerequisiteState.Missing) return;

            if (_prerequisiteRefresh is { IsCompleted: false }) return;
            if (_prerequisiteRefresh != null && Stopwatch.GetElapsedTime(_prerequisiteRefreshTimestamp) < PrerequisiteMinInterval) return;

            _prerequisiteRefreshTimestamp = Stopwatch.GetTimestamp();
            _prerequisiteRefresh = info.RefreshPrerequisitesAsync();
        }

        // one shot; Restart pushes it back while events keep coming
        private DispatcherQueueTimer CreateSettleTimer(TimeSpan settleTime, Action refresh)
        {
            var timer = _dispatcherQueue!.CreateTimer();
            timer.Interval = settleTime;
            timer.IsRepeating = false;
            timer.Tick += (s, e) => refresh();
            return timer;
        }

        private static void Restart(DispatcherQueueTimer? timer)
        {
            if (timer == null) return;
            timer.Stop();
            timer.Start();
        }
    }
}
