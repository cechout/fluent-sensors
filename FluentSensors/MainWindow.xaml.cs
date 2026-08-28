using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WinUIEx;

using FluentSensors.Controls.SensorRow;
using FluentSensors.Core;
using FluentSensors.Core.StaticInfo;
using FluentSensors.Features.AppStatus;
using FluentSensors.Features.Performance;
using FluentSensors.Features.Sensors;
using FluentSensors.Features.Settings;
using FluentSensors.Features.Widget;
using FluentSensors.Persistence.Services;
using FluentSensors.Common.Sensors;


namespace FluentSensors
{
    public sealed partial class MainWindow : Window
    {
        // === win32 api imports ===

        // --- workaround: hiding a window in WinUI 3 ---
        // problem: this.Hide() alone does not remove the window from Alt+Tab or the taskbar switcher reliably; the official
        // AppWindow.IsShownInSwitchers API was tried first and failed the same way; no public issue
        // found that documents this exact behavior
        // fix: manually apply WS_EX_TOOLWINDOW (removes it from Alt+Tab) and WS_EX_NOACTIVATE (prevents Windows from auto-
        // focusing it) via SetWindowLongW, then SetWindowPos with SWP_FRAMECHANGED to apply the new styles
        // own solution found through trial and error, see usage in AppWindow_Closing/OpenDashboard

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static partial int GetWindowLong(IntPtr hWnd, int nIndex);
        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        // --- workaround: OpenDashboard does not reliably come to the front ---
        // problem: WinUI 3 Window.Activate() fails to bring a window to the foreground if it is already restored but sitting
        // in the background of other windows;
        // Only works correctly starting from a minimized state confirmed, still-open platform bug:
        // https://github.com/microsoft/microsoft-ui-xaml/issues/7595
        // hits us specifically when the widget window grabbed foreground moments earlier (tray double click), since Restore()
        // puts the main window into exactly that broken background-but-not-minimized state right before Activate() runs
        // fix: call the raw Win32 SetForegroundWindow directly instead of relying on Activate() for the actual foreground grab
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetForegroundWindow(IntPtr hWnd);


        // === fields ===

        public static MainWindow CurrentInstance { get; private set; }
        private const string WindowKey = "Main"; // key under which this windows state is saved
        private bool _isForceClosing = false;
        private bool _isHardwareServiceLoaded = false;
        private bool _isDashboardClosed = false;

        // system tray icon commands
        public XamlUICommand RestoreAppCommand { get; } = new XamlUICommand(); // restore
        public XamlUICommand ShowMainWindowCommand { get; } = new XamlUICommand(); // restore + navigate to SensorPage
        public XamlUICommand OpenPerformanceCommand { get; } = new XamlUICommand(); // restore + navigate to PerformancePage
        public XamlUICommand OpenSettingsCommand { get; } = new XamlUICommand(); // restore + navigate to SettingsPage
        public XamlUICommand ExitAppCommand { get; } = new XamlUICommand();
        public XamlUICommand TrayLeftClickCommand { get; } = new XamlUICommand(); // tray single click, restores widget only
        public XamlUICommand TrayDoubleClickCommand { get; } = new XamlUICommand(); // tray double click, restores main window only

        // backs the title bar status readout (sensors found/rendering, CPU/RAM/handles); AppStatusService itself
        // is started further down, once hardware discovery has actually run
        public AppStatusViewModel AppStatus { get; } = new AppStatusViewModel();


        // === constructor ===

        public MainWindow()
        {
            // initialization
            this.InitializeComponent();
            this.AppWindow.SetIcon("Assets\\Icon\\Icon.ico");
            CurrentInstance = this;

            // AppWindow configuration
            // titlebar 
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            if (AppWindow.TitleBar.ExtendsContentIntoTitleBar)
            {
                AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
                AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

            }

            // AppTitleBar is a plain Grid now, not the native TitleBar control, so it needs to be registered as the
            // drag region explicitly; interactive controls inside it (the toggle button, info popup buttons) stay
            // clickable on their own, only the empty space around them is actually draggable
            this.SetTitleBar(AppTitleBar);

            var manager = WinUIEx.WindowManager.Get(this);
            manager.MinWidth = 600;
            manager.MinHeight = 400;

            // size and position: restore the last saved rect, or fall back to the original defaults
            var savedState = WindowStateService.Instance.GetState(WindowKey);
            if (savedState != null)
            {
                this.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                savedState.X, savedState.Y, savedState.Width, savedState.Height));

                if (savedState.IsMaximized && this.AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Maximize();
                }
            }
            else
            {
                this.SetWindowSize(650, 770); // width, height
                this.CenterOnScreen();
                var currentPos = this.AppWindow.Position;
                // yea idk; might change this in future
                this.AppWindow.Move(new Windows.Graphics.PointInt32(currentPos.X - 400, currentPos.Y - 100));
            }

            // theming
            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            ApplyTitleBarTheme(SettingsService.Instance.AppTheme);
            ApplyTrayIconTheme(SettingsService.Instance.AppTheme);
            ApplyTheme(SettingsService.Instance.AppTheme);

            // window lifecycle events
            this.Closed += (s, args) =>
            {
                SettingsService.Instance.ThemeChanged -= OnThemeChanged;
                CurrentInstance = null;
            };
            ((FrameworkElement)this.Content).Loaded += MainWindow_Loaded;
            this.AppWindow.Changed += AppWindow_Changed;
            this.AppWindow.Closing += AppWindow_Closing;

            // system tray commands 
            RestoreAppCommand.ExecuteRequested += (s, e) => RestoreApp();
            ShowMainWindowCommand.ExecuteRequested += (s, e) =>
            {
                RestoreApp();
                MainNavigationView.SelectedItem = MainNavigationView.MenuItems[0];
            };
            OpenPerformanceCommand.ExecuteRequested += (s, e) =>
            {
                RestoreApp();
                MainNavigationView.SelectedItem = MainNavigationView.MenuItems[1];
            };
            OpenSettingsCommand.ExecuteRequested += (s, e) =>
            {
                RestoreApp();
                MainNavigationView.SelectedItem = MainNavigationView.FooterMenuItems[0];
            };
            TrayLeftClickCommand.ExecuteRequested += (s, e) => WidgetWindow.RestoreIfOpen();
            TrayDoubleClickCommand.ExecuteRequested += (s, e) => OpenDashboard();
            ExitAppCommand.ExecuteRequested += (s, e) => QuitAppNow(); // tray menu "Exit"


            // TEMP: uncomment to dump everything WinStaticInfoService collected to the Debug output window
            // _ = Task.Run(FluentSensors.Diagnostics.WinStaticInfoDebugDump.Dump);

            // TEMP: uncomment to dump the taskbar detection backend (WinTaskbarService/WinTaskbarUiaProbe/
            // WinShellStateWatcher) to the Debug output window
            _ = Task.Run(FluentSensors.Diagnostics.WinTaskbarDebugDump.Dump);
        }


        // === lifecycle and initialization ===

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isHardwareServiceLoaded) return;
            _isHardwareServiceLoaded = true;

            await StartHardwareServiceAsync(); // load the HardwareMonitorService singleton instance asynchronously
        }

        private async Task StartHardwareServiceAsync()
        {
            var monitor = HardwareMonitorService.Instance;

            // kicks off static hardware info collection (WMI queries) on a background thread; fully independent and
            // parallel to the lhm sensor init below
            // captured instead of fire-and-forget: without waiting on this, a page that accesses
            // WinStaticInfoService.Instance before this finishes blocks its own thread for the remainder of the WMI
            // scan (Lazy<T> just makes every other accessor wait for the same in-progress construction); awaited
            // together with the sensor data wait further down, so a slow WMI scan still shows up as splash progress
            // instead of surfacing later as an unexplained freeze on whichever page asks for it first
            var staticInfoPrewarmTask = Task.Run(() => WinStaticInfoService.Instance);

            // scan motherboard
            LoadingStatusText.Text = "Initializing motherboard...";
            LoadingProgressBar.Value = 15;
            await monitor.InitMotherboardAsync();

            // scan CPU
            LoadingStatusText.Text = "Scanning CPU...";
            LoadingProgressBar.Value = 30;
            await monitor.InitCpuAsync();

            // scan GPU
            LoadingStatusText.Text = "Scanning GPU...";
            LoadingProgressBar.Value = 45;
            await monitor.InitGpuAsync();

            // scan memory and storage
            LoadingStatusText.Text = "Checking memory and storage...";
            LoadingProgressBar.Value = 60;
            await monitor.InitMemoryAndStorageAsync();

            // scan dedicated fan/aio controllers (e.g. Aquacomputer, Corsair Commander, NZXT Kraken)
            LoadingStatusText.Text = "Scanning controllers...";
            LoadingProgressBar.Value = 75;
            await monitor.InitControllerAsync();

            // scan network adapters (Wi-Fi, Ethernet, and any virtual adapters Windows reports)
            LoadingStatusText.Text = "Scanning network adapters...";
            LoadingProgressBar.Value = 100;
            await monitor.InitNetworkAsync();

            // no we start the HardwareMonitorService loop manually
            monitor.StartMonitoring();

            // sensor discovery just finished above, LhmHardwareTreeService starts filling in from here on
            AppStatusService.Instance.Start();

            // we explicitly wait until the ViewModel has received and processed the very first data payload, and until
            // the static info prewarm above has finished; both have been running in parallel with everything since
            // their own starting point, so this only waits as long as whichever of the two is still slower
            LoadingStatusText.Text = "Waiting for data...";
            await Task.WhenAll(
                SensorsViewModel.Instance.WaitForInitialLoadAsync(),
                staticInfoPrewarmTask);

            // now we are finished loading
            LoadingStatusText.Text = "Ready";
            //await Task.Delay(100);

            // show the main grid
            MainNavigationView.Visibility = Visibility.Visible;

            // manually close navigation pane
            this.DispatcherQueue.TryEnqueue(() =>
            {
                MainNavigationView.IsPaneOpen = false;
            });
            await Task.Delay(200);

            SplashOverlay.Visibility = Visibility.Collapsed;
            AppStatus.IsAppReady = true;
            AppStatus.IsDotNetRuntimeMissing = !WinStaticInfoService.Instance.IsDotNetRuntimeInstalled;
            MainNavigationView.SelectedItem = MainNavigationView.MenuItems[0];

            // re-open the widget window with its previously pinned sensors, if it was still open when the app last closed
            TryRestoreWidgetWindow();

            // re-open the taskbar widget with its pinned sensors if any are configured
            TryRestoreTaskbarWidgetWindow();
        }

        // re-creates the widget window with whichever previously pinned sensors still exist on
        // this system, but only if it was actually open when the app last closed
        private void TryRestoreWidgetWindow()
        {
            var widgetState = WindowStateService.Instance.GetState("Widget");
            if (widgetState == null || !widgetState.WasOpen) return;

            var pinnedSensorIds = SensorSelectionService.Instance.GetSelection(SensorSelectionProfile.WidgetWindow);
            if (pinnedSensorIds.Count == 0) return;

            var pinnedSensors = FindSensorRowsByIds(pinnedSensorIds);
            if (pinnedSensors.Count == 0) return; // none of them exist on this system anymore

            WidgetWindow.ShowWithSensors(pinnedSensors);
        }

        // re-creates the taskbar widget with whichever sensors are currently pinned under the taskbar profile
        private void TryRestoreTaskbarWidgetWindow()
        {
            var pinnedSensorIds = SensorSelectionService.Instance.GetSelection(SensorSelectionProfile.Taskbar);
            if (pinnedSensorIds.Count == 0) return;

            var pinnedSensors = FindSensorRowsByIds(pinnedSensorIds);
            if (pinnedSensors.Count == 0) return;

            FluentSensors.Features.TaskbarWidget.TaskbarWidgetWindow.ShowWithSensors(pinnedSensors);
        }

        // looks up live SensorRowViewModel instances (visible or hidden) by their saved IDs, preserving the original
        // pin order rather than whatever order the hardware groups produce
        private List<SensorRowViewModel> FindSensorRowsByIds(IReadOnlyList<string> ids)
        {
            var allSensors = SensorsViewModel.Instance.HardwareGroups
                .SelectMany(g => g.Sensors.Concat(g.HiddenSensors));

            return ids
                .Select(id => allSensors.FirstOrDefault(s => s.Id == id))
                .Where(s => s != null)
                .ToList();
        }


        // === app status readout ===

        // feeds AppStatus.HasEnoughWidthForFull, which decides whether the windows group still fits next to the
        // lhm group; fires on every window resize, see AppStatusViewModel.UpdateVisibility for the combined logic
        private void AppTitleBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            AppStatus.UpdateAvailableWidth(e.NewSize.Width);
        }

        // plain Button standing in for a real ToggleButton, see the XAML comment on it for why
        private void StatusToggleButton_Click(object sender, RoutedEventArgs e)
        {
            AppStatus.IsStatusEnabled = !AppStatus.IsStatusEnabled;
        }

        private Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        private double GetToggleOpacity(bool enabled) => enabled ? 1.0 : 0.5;


        // === theme handling ===

        private void OnThemeChanged(string newTheme)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                ApplyTitleBarTheme(newTheme);
                ApplyTrayIconTheme(newTheme);
                ApplyTheme(newTheme);
            });
        }

        private void ApplyTitleBarTheme(string themeTag)
        {
            AppWindow.TitleBar.PreferredTheme = themeTag switch
            {
                "Light" => Microsoft.UI.Windowing.TitleBarTheme.Light,
                "Dark" => Microsoft.UI.Windowing.TitleBarTheme.Dark,
                _ => Microsoft.UI.Windowing.TitleBarTheme.UseDefaultAppMode
            };
        }

        private void ApplyTrayIconTheme(string themeTag) // theme switch does not work smh
        {
            var targetTheme = themeTag switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            TrayIcon.RequestedTheme = targetTheme;
        }

        private void ApplyTheme(string themeTag)
        {
            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = themeTag switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }
        }


        // === navigation ===

        private void MainNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            // checks if native settings item got clicked
            if (args.IsSettingsSelected)
            {
                //contentFrame.Navigate(typeof(SettingsPage));
                return;
            }

            if (args.SelectedItem is NavigationViewItem selectedItem)
            {
                string pageTag = selectedItem.Tag.ToString(); 
                switch (pageTag)
                {
                    case "Sensors":
                        contentFrame.Navigate(typeof(SensorsPage));
                        break;

                    case "Settings":
                        contentFrame.Navigate(typeof(SettingsPage));
                        break;

                    case "Performance":
                        contentFrame.Navigate(typeof(PerformancePage));
                        break;
                }
            }
        }


        // === window state and system tray ===

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            // during a forced shutdown (settings reset/import -> restart), any write here would use in-memory state that
            // is stale relative to whatever was just written to disk, and would silently overwrite it; the app is about to
            // die anyway, nothing here needs to be saved
            if (_isForceClosing) return;

            if (args.DidPresenterChange)
            {
                CheckAndHideToTray();
            }
            if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange)
            {
                SaveWindowState();
            }

            // minimize/restore only shows up as DidSizeChange (see the same workaround already in
            // WidgetWindow.AppWindow_Changed), actual hide/show (e.g. minimize-to-tray via CheckAndHideToTray
            // above) shows up as DidVisibilityChange instead; checked after CheckAndHideToTray so a Hide() it just
            // triggered is already reflected in AppWindow.IsVisible below
            if (args.DidSizeChange || args.DidVisibilityChange)
            {
                UpdatePerformancePageRenderingState();
            }
        }

        // pauses/resumes the Performance pages own rendering gate whenever this window itself stops or starts
        // actually being shown on screen (minimized, or hidden entirely e.g. minimize-to-tray); a no-op whenever
        // Performance page is not the current contentFrame content, PerformancePage tracks its own default state
        // for whenever it is next navigated to
        private void UpdatePerformancePageRenderingState()
        {
            if (contentFrame.Content is not PerformancePage performancePage) return;

            bool isMinimized = this.AppWindow.Presenter is OverlappedPresenter presenter &&
                               presenter.State == OverlappedPresenterState.Minimized;

            performancePage.SetWindowVisibilityActive(this.AppWindow.IsVisible && !isMinimized);
        }

        public void CheckAndHideToTray()
        {
            // check if user toggled the system tray functionality
            if (!SettingsService.Instance.MinimizeToTray) return;

            // main window is ready for tray if its explicitly closed, already hidden, or currently minimized
            bool isMainReady = _isDashboardClosed || !this.AppWindow.IsVisible ||
                               (this.AppWindow.Presenter is OverlappedPresenter opMain && opMain.State == OverlappedPresenterState.Minimized);

            // widget window is ready if it does not exist, is hidden, or is minimized
            bool isWidgetReady = true;
            if (WidgetWindow.CurrentInstance != null)
            {
                var opWidget = WidgetWindow.CurrentInstance.AppWindow.Presenter as OverlappedPresenter;
                isWidgetReady = !WidgetWindow.CurrentInstance.AppWindow.IsVisible || (opWidget != null && opWidget.State == OverlappedPresenterState.Minimized);
            }

            // if both windows are out of the way, hide the app completely from the taskbar
            if (isMainReady && isWidgetReady)
            {
                // only call hide if it's not already locked down by the Win32 closing shield
                if (!_isDashboardClosed)
                {
                    this.Hide();
                }

                if (WidgetWindow.CurrentInstance != null)
                {
                    WidgetWindow.CurrentInstance.Hide();
                }
            }
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // if user clicks "exit app" in the tray menu, actually kill the process
            if (_isForceClosing) return;

            if (SettingsService.Instance.MinimizeToTray)
            {
                // cancel the actual shutdown
                args.Cancel = true;
                _isDashboardClosed = true;

                // applies the Win32 shield, see workaround comment on the P/Invoke declarations above
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

                this.Hide();
                CheckAndHideToTray();
            }
            else
            {
                // MinimizeToTray is off: closing the main window always fully exits the app right away, no matter what
                // other windows are still open
                // WidgetWindow.AppWindow_Closing always cancels its own close, then hides and retains itself (the
                // retained-instance memory leak workaround), so without a hard kill here the process never actually
                // terminates while a widget is pinned
                // StopMonitoring alone used to leave everything stuck with a frozen, dataless widget and no way back
                QuitAppNow();
            }
        }

        public void OpenDashboard()
        {
            // release the lock
            _isDashboardClosed = false;

            // remove the Win32 shields to make it a normal app window again
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TOOLWINDOW & ~WS_EX_NOACTIVATE);

            this.Show();
            if (this.AppWindow.Presenter is OverlappedPresenter opMain)
            {
                opMain.Restore();
            }
            this.Activate();
            SetForegroundWindow(hwnd); // see workaround comment on the P/Invoke declaration above
        }

        private void RestoreApp()
        {
            // triggered by system tray double click
            // only wake up the main window if the user didn't explicitly close it via "X"
            if (!_isDashboardClosed)
            {
                OpenDashboard();
            }

            // always wake up the widget window if it exists
            if (WidgetWindow.CurrentInstance != null)
            {
                WidgetWindow.CurrentInstance.Show();
                if (WidgetWindow.CurrentInstance.AppWindow.Presenter is OverlappedPresenter opWidget)
                {
                    opWidget.Restore();
                }
                WidgetWindow.CurrentInstance.Activate();
            }
        }

        // hard-kills the process right now instead of going through the normal WinUI Closing/Exit path
        // used both by the tray Exit command and by closing the main window while MinimizeToTray is off
        //
        // Application.Current.Exit() was tried first for both cases but is unreliable with multiple windows open
        private void QuitAppNow()
        {
            _isForceClosing = true;
            SaveWindowState();
            PersistenceService.Instance.FlushAll();
            Process.GetCurrentProcess().Kill();
        }

        // controlled tear-down for scenarios that bypass the normal closing paths (e.g. settings reset -> app restart)

        // --- workaround: second instance survives an automatic restart ---
        // problem: Application.Current.Exit() does not reliably terminate the process in every scenario; documented upstream
        // for the case where Exit() is called while no window is open/activated
        // (https://github.com/microsoft/microsoft-ui-xaml/issues/5931)
        // our repro is not identical to that thread, but the settings-import restart hits this in the same state, no active
        // window left, and produced the same result: two full instances running
        // fix: hard-kill the process instead of Exit(); only needed for this one restart path
        public void ForceExit()
        {
            _isForceClosing = true;

            // --- workaround: Kill() never reached ---
            // problem: HardwareMonitorService.Cleanup() -> Computer.Close() can hang indefinitely; it unloads the WinRing0
            // kernel driver via the SCM while the restarted process races for the same driver handle
            // no public issue found for this exact case, likely specific to LibreHardwareMonitorLib + elevated process
            // fix: skip Cleanup() entirely on this path; found by moving Kill() to the first line of ForceExit and
            // confirming the hang disappeared; the OS releases the driver handle once the process is gone
            //
            // flush must happen before Kill(): a hard kill skips finalizers and any Closing/Exit handlers, so this
            // is the last point in-memory state can reach disk
            // running elevated (required for the hardware driver) is what forces this whole detour - a non-elevated app could
            // just rely on Exit() and normal teardown
            //
            // deliberately no SaveWindowState() here: a fresh window-state reset should not get immediately overwritten by
            // a final position save on the way out
            PersistenceService.Instance.FlushAll();
            Process.GetCurrentProcess().Kill();
        }

        // captures the current position/size and writes it (debounced) to the window state store
        // skipped while minimized or hidden in the tray, since those transient rects would overwrite a perfectly good
        // saved state with garbage
        private void SaveWindowState()
        {
            var presenter = this.AppWindow.Presenter as OverlappedPresenter;
            bool isMinimized = presenter != null && presenter.State == OverlappedPresenterState.Minimized;
            if (isMinimized || !this.AppWindow.IsVisible) return;

            bool isMaximized = presenter != null && presenter.State == OverlappedPresenterState.Maximized;

            // while maximized, keep the last known "restored" rect instead of overwriting it with
            // the maximized bounds, so un-maximizing later returns to the right size
            var existing = WindowStateService.Instance.GetState(WindowKey) ?? new Persistence.Models.WindowState();
            var newState = new Persistence.Models.WindowState
            {
                X = isMaximized ? existing.X : this.AppWindow.Position.X,
                Y = isMaximized ? existing.Y : this.AppWindow.Position.Y,
                Width = isMaximized ? existing.Width : this.AppWindow.Size.Width,
                Height = isMaximized ? existing.Height : this.AppWindow.Size.Height,
                IsMaximized = isMaximized
            };

            WindowStateService.Instance.SetState(WindowKey, newState);
        }
    }
}
