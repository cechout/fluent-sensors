using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
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
using FluentSensors.Core.Startup;
using FluentSensors.Core.Update;
using FluentSensors.Features.AppStatus;
using FluentSensors.Features.CsvLogging;
using FluentSensors.Features.Performance;
using FluentSensors.Features.Sensors;
using FluentSensors.Features.Start;
using FluentSensors.Features.Settings;
using FluentSensors.Features.Update;
using FluentSensors.Features.Widget;
using FluentSensors.Persistence.Models;
using FluentSensors.Persistence.Services;
using FluentSensors.Common.Localization;
using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;


namespace FluentSensors
{
    public sealed partial class MainWindow : Window
    {
        // === win32 api imports ===

        // --- workaround: hiding a window in WinUI 3 ---
        // problem: Hide() alone does not reliably drop the window from Alt+Tab and the taskbar switcher, and
        // AppWindow.IsShownInSwitchers fails the same way; no public issue found
        // fix: add WS_EX_TOOLWINDOW (off Alt+Tab) and WS_EX_NOACTIVATE (no auto focus), then SetWindowPos with
        // SWP_FRAMECHANGED; see ApplyHideShield and OpenDashboard

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
        // problem: Window.Activate() brings a minimized window to the foreground, but not a restored one behind others
        // (confirmed, still open); hits after the widget took the foreground moments earlier (tray double click):
        // https://github.com/microsoft/microsoft-ui-xaml/issues/7595
        // fix: the raw SetForegroundWindow does the foreground grab
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetForegroundWindow(IntPtr hWnd);

        // the precondition of the startup focus handback, see ReclaimForeground
        [LibraryImport("user32.dll")]
        private static partial IntPtr GetForegroundWindow();


        // === fields ===

        public static MainWindow CurrentInstance { get; private set; }
        private const string WindowKey = "Main";
        private const string ProjectPageUrl = "https://github.com/cechout/fluent-sensors"; // readme, also linked from the settings page
        private bool _isForceClosing = false;
        private bool _isHardwareServiceLoaded = false;
        private bool _isDashboardClosed = false;

        // the one navigation after the splash, which gets the entrance transition
        private bool _isStartupNavigation = false;

        // profile a caller asked for while the splash was still running; applied once the sensor page exists
        private SensorSelectionProfile? _pendingSensorProfile = null;

        // the same for a hardware group request
        private IReadOnlyList<string> _pendingSensorHardware = null;

        // and for the taskbar settings, from the gear button of the taskbar flyout
        private bool _isTaskbarSettingsPending = false;

        // system tray icon commands
        public XamlUICommand RestoreAppCommand { get; } = new XamlUICommand();
        public XamlUICommand ShowMainWindowCommand { get; } = new XamlUICommand(); // restore; sensors page
        public XamlUICommand OpenPerformanceCommand { get; } = new XamlUICommand(); // restore; performance page
        public XamlUICommand OpenSettingsCommand { get; } = new XamlUICommand(); // restore; settings page
        public XamlUICommand ShowCsvWindowCommand { get; } = new XamlUICommand(); // the csv logger only
        private readonly MenuFlyoutItem[] _widgetTrayItems = new MenuFlyoutItem[WidgetWindow.MaxWidgetWindows]; // one widget only, each
        public XamlUICommand OpenDocumentationCommand { get; } = new XamlUICommand(); // the project page in the browser
        public XamlUICommand ExitAppCommand { get; } = new XamlUICommand();
        public XamlUICommand TrayLeftClickCommand { get; } = new XamlUICommand(); // single click; open readouts
        public XamlUICommand TrayDoubleClickCommand { get; } = new XamlUICommand(); // double click; main window

        // the title bar status readout; (AppStatusService starts once hardware discovery has run)
        public AppStatusViewModel AppStatus { get; } = new AppStatusViewModel();

        // the real window rect while a launch straight to the tray parks it off screen
        private Windows.Graphics.RectInt32? _hiddenStartupBounds;

        // title bar columns of the two status groups, swapped with the configured order; (the leading group keeps
        // the smaller gap to the toggle button)
        private const int LeadingStatusGroupColumn = 5;
        private const int TrailingStatusGroupColumn = 6;
        private static readonly Thickness LeadingStatusGroupMargin = new Thickness(8, 0, 0, 0);
        private static readonly Thickness TrailingStatusGroupMargin = new Thickness(12, 0, 0, 0);

        // in DIP; the last measured width of each status group, kept while it is hidden, and the gap the readout keeps
        // to the caption buttons
        private double _lhmStatusGroupWidth;
        private double _windowsStatusGroupWidth;
        private const double StatusCaptionGap = 12;


        // === constructor ===

        public MainWindow()
        {
            this.InitializeComponent();
            this.AppWindow.SetIcon("Assets\\Icon\\Icon.ico");
            CurrentInstance = this;

            // a click on empty space hides the keyboard focus rectangle again
            PointerFocusReset.Attach(Content);

            // title bar
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            if (AppWindow.TitleBar.ExtendsContentIntoTitleBar)
            {
                AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
                AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

            }

            // AppTitleBar is a plain Grid registered as the drag region; its interactive controls get passthrough
            // rects, see RefreshTitleBarLayout
            this.SetTitleBar(AppTitleBar);

            var manager = WinUIEx.WindowManager.Get(this);
            manager.MinWidth = 600;
            manager.MinHeight = 400;

            // size and position: restore the last saved rect, or fall back to the defaults in AppSettingsData
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
                this.SetWindowSize(AppSettingsData.MainWindowDefaultWidthDip, AppSettingsData.MainWindowDefaultHeightDip);
                this.CenterOnScreen();
            }

            // a launch straight to the tray is set up before anything is on screen: Activate() is what loads the
            // content, so the window is parked far off screen behind the hide shield instead (not minimized, which has
            // broken the SkiaSharp graph surfaces before; off screen the layout runs as normal)
            if (StartsHiddenInTray())
            {
                _hiddenStartupBounds = new Windows.Graphics.RectInt32(
                    this.AppWindow.Position.X, this.AppWindow.Position.Y,
                    this.AppWindow.Size.Width, this.AppWindow.Size.Height);

                ApplyHideShield();
                this.AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
            }

            // theming
            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            ApplyTitleBarTheme(SettingsService.Instance.AppTheme);
            ApplyTrayIconTheme(SettingsService.Instance.AppTheme);
            ApplyTheme(SettingsService.Instance.AppTheme);

            // title bar status readout; the settings page owns which groups are shown and in which order
            SettingsService.Instance.StatusReadoutChanged += OnStatusReadoutChanged;

            // window lifecycle events
            this.Closed += (s, args) =>
            {
                SettingsService.Instance.ThemeChanged -= OnThemeChanged;
                SettingsService.Instance.StatusReadoutChanged -= OnStatusReadoutChanged;
                WidgetWindow.WidgetStateChanged -= UpdateWidgetTrayItems;
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
                MainNavigationView.SelectedItem = SensorsNavItem;
            };
            OpenPerformanceCommand.ExecuteRequested += (s, e) =>
            {
                RestoreApp();
                MainNavigationView.SelectedItem = PerformanceNavItem;
            };
            OpenSettingsCommand.ExecuteRequested += (s, e) =>
            {
                RestoreApp();
                MainNavigationView.SelectedItem = SettingsNavItem;
            };
            ShowCsvWindowCommand.ExecuteRequested += (s, e) => CsvLoggerWindow.RestoreIfOpen();
            OpenDocumentationCommand.ExecuteRequested += (s, e) => OpenProjectPage();

            // one entry per possible widget window, numbered through one format string; each restores its window only
            for (int index = 0; index < _widgetTrayItems.Length; index++)
            {
                int widgetIndex = index;
                var command = new XamlUICommand();
                command.ExecuteRequested += (s, e) => WidgetWindow.RestoreIfOpen(widgetIndex);
                _widgetTrayItems[index] = new MenuFlyoutItem
                {
                    Text = AppStrings.Format("Main_WidgetWindowTrayItem", index + 1),
                    Command = command
                };
            }
            WidgetWindow.WidgetStateChanged += UpdateWidgetTrayItems;
            UpdateWidgetTrayItems();

            // the restores are no-ops for a closed window, so one click brings back whatever is open
            TrayLeftClickCommand.ExecuteRequested += (s, e) =>
            {
                WidgetWindow.RestoreAllOpen();
                CsvLoggerWindow.RestoreIfOpen();
            };
            TrayDoubleClickCommand.ExecuteRequested += (s, e) => OpenDashboard();
            ExitAppCommand.ExecuteRequested += (s, e) => QuitAppNow(); // tray menu "Exit"


            // TEMP: uncomment to dump WinStaticInfoService to the Debug output window
            // _ = Task.Run(FluentSensors.Diagnostics.WinStaticInfoDebugDump.Dump);

            // TEMP: uncomment to dump the taskbar detection to the Debug output window
            // _ = Task.Run(FluentSensors.Diagnostics.WinTaskbarDebugDump.Dump);
        }


        // === lifecycle and initialization ===

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isHardwareServiceLoaded) return;
            _isHardwareServiceLoaded = true;

            // ends the off screen parking: hidden for real first, then moved back so it opens in place from the tray
            if (_hiddenStartupBounds is Windows.Graphics.RectInt32 bounds)
            {
                HideToTray();
                this.AppWindow.MoveAndResize(bounds);
                _hiddenStartupBounds = null;
            }

            await StartHardwareServiceAsync();
        }

        private async Task StartHardwareServiceAsync()
        {
            var monitor = HardwareMonitorService.Instance;

            // static hardware info (WMI) in parallel to the sensor init; awaited with the first data below, so a slow
            // scan shows as splash time rather than as a freeze on whichever page touches it first (Lazy<T> blocks)
            var staticInfoPrewarmTask = Task.Run(() => WinStaticInfoService.Instance);

            // scan motherboard
            LoadingStatusText.Text = AppStrings.Get("Main_LoadingMotherboard");
            LoadingProgressBar.Value = 15;
            await monitor.InitMotherboardAsync();

            // scan CPU
            LoadingStatusText.Text = AppStrings.Get("Main_LoadingCpu");
            LoadingProgressBar.Value = 30;
            await monitor.InitCpuAsync();

            // scan GPU
            LoadingStatusText.Text = AppStrings.Get("Main_LoadingGpu");
            LoadingProgressBar.Value = 45;
            await monitor.InitGpuAsync();

            // scan memory and storage
            LoadingStatusText.Text = AppStrings.Get("Main_LoadingMemoryAndStorage");
            LoadingProgressBar.Value = 60;
            await monitor.InitMemoryAndStorageAsync();

            // scan fan and aio controllers (Aquacomputer, Corsair Commander, NZXT Kraken)
            LoadingStatusText.Text = AppStrings.Get("Main_LoadingControllers");
            LoadingProgressBar.Value = 75;
            await monitor.InitControllerAsync();

            // scan network adapters (virtual ones included)
            LoadingStatusText.Text = AppStrings.Get("Main_LoadingNetwork");
            LoadingProgressBar.Value = 100;
            await monitor.InitNetworkAsync();

            // the polling loop
            monitor.StartMonitoring();

            // discovery is done, the hardware tree fills in from here
            AppStatusService.Instance.Start();

            // the first data payload and the static info prewarm, whichever is slower
            LoadingStatusText.Text = AppStrings.Get("Main_LoadingWaitingForData");
            await Task.WhenAll(
                SensorsViewModel.Instance.WaitForInitialLoadAsync(),
                staticInfoPrewarmTask);

            LoadingStatusText.Text = AppStrings.Get("Main_LoadingReady");

            // manually close navigation pane
            this.DispatcherQueue.TryEnqueue(() =>
            {
                MainNavigationView.IsPaneOpen = false;
            });
            await Task.Delay(200);

            // show the main grid, both in the same frame so the transparent splash never uncovers the pane
            MainNavigationView.Opacity = 1;
            SplashOverlay.Visibility = Visibility.Collapsed;
            AppStatus.IsAppReady = true;
            AppStatus.IsDotNetRuntimeMissing = !WinStaticInfoService.Instance.IsDotNetRuntimeInstalled;
            AppStatus.IsPawnIoMissing = !WinStaticInfoService.Instance.IsPawnIoInstalled;
            // a profile or taskbar settings request from the splash outranks the startup page; (the blocks below need
            // the page)
            _isStartupNavigation = true;
            MainNavigationView.SelectedItem = _pendingSensorProfile != null ? SensorsNavItem
                : _isTaskbarSettingsPending ? SettingsNavItem
                : StartupNavItem();

            // requests from the splash; the selection above created the page
            if (_pendingSensorProfile is SensorSelectionProfile pendingProfile
                && contentFrame.Content is SensorsPage pendingPage)
            {
                pendingPage.SelectProfile(pendingProfile);
                _pendingSensorProfile = null;
            }

            if (_pendingSensorHardware != null && contentFrame.Content is SensorsPage pendingGroupPage)
            {
                pendingGroupPage.ExpandHardwareGroup(_pendingSensorHardware);
                _pendingSensorHardware = null;
            }

            if (_isTaskbarSettingsPending && contentFrame.Content is SettingsPage pendingSettingsPage)
            {
                pendingSettingsPage.ScrollToTaskbarSection();
                _isTaskbarSettingsPending = false;
            }

            // read before the two restores below take the focus, see ReclaimForeground
            bool hadForeground = GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(this);

            TryRestoreWidgetWindows();
            TryRestoreTaskbarWidgetWindow();

            if (hadForeground)
            {
                ReclaimForeground();
            }

            // a moved or reinstalled copy leaves the scheduled task on the old exe; (off the UI thread, the check
            // costs two schtasks processes)
            if (SettingsService.Instance.RunOnStartup)
            {
                _ = Task.Run(() => WinAutostartService.RepairIfStale(SettingsService.Instance.DelayStartup));
            }

            // last, so the update check never competes with sensor discovery
            UpdateService.Instance.UpdateStateChanged += OnUpdateStateChanged;
            UpdateService.Instance.Start(WinRT.Interop.WindowNative.GetWindowHandle(this));
        }

        // reopens every widget with the pinned sensors that still exist, if it was open when the app last closed
        private void TryRestoreWidgetWindows()
        {
            for (int index = 0; index < WidgetWindow.MaxWidgetWindows; index++)
            {
                var widgetState = WindowStateService.Instance.GetState(WidgetWindow.GetWindowKey(index));
                if (widgetState == null || !widgetState.WasOpen) continue;

                var pinnedSensorIds = SensorSelectionService.Instance.GetSelection(SensorSelectionProfile.WidgetWindow, index);
                if (pinnedSensorIds.Count == 0) continue;

                var pinnedSensors = FindSensorRowsByIds(pinnedSensorIds);
                if (pinnedSensors.Count == 0) continue; // none exist on this system any more

                WidgetWindow.ShowWithSensors(index, pinnedSensors);
            }
        }

        // the same for the taskbar widget and the taskbar profile
        private void TryRestoreTaskbarWidgetWindow()
        {
            var taskbarState = WindowStateService.Instance.GetState("TaskbarWidget");
            if (taskbarState == null || !taskbarState.WasOpen) return;

            var pinnedSensorIds = SensorSelectionService.Instance.GetSelection(SensorSelectionProfile.Taskbar);
            if (pinnedSensorIds.Count == 0) return;

            var pinnedSensors = FindSensorRowsByIds(pinnedSensorIds);
            if (pinnedSensors.Count == 0) return;

            FluentSensors.Features.TaskbarWidget.TaskbarWidgetWindow.ShowWithSensors(pinnedSensors);
        }

        // hands the foreground back after the readout windows took it with their Activate(); otherwise the main
        // window stays in front but inactive, and its Mica drops to the flat fallback color
        // (only called when it held the foreground before, which keeps a launch to the tray out of this)
        private void ReclaimForeground()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            this.Activate();
            SetForegroundWindow(hwnd); // see the workaround on the P/Invoke
        }

        // live rows (visible or hidden) for the saved ids, in discovery order like a live pin; (the saved list is in
        // toggle order, not in pinned order)
        private List<SensorRowViewModel> FindSensorRowsByIds(IReadOnlyList<string> ids)
        {
            var wantedIds = new HashSet<string>(ids);

            return SensorsViewModel.Instance.HardwareGroups
                .SelectMany(g => g.Sensors.Concat(g.HiddenSensors))
                .Where(s => wantedIds.Contains(s.Id))
                .ToList();
        }


        // === app status readout ===

        private void AppTitleBar_Loaded(object sender, RoutedEventArgs e)
        {
            // the pill and the hints appear by binding, not by resizing the bar, so the bar SizeChanged misses them
            UpdateButton.SizeChanged += OnTitleBarExtraSizeChanged;
            DotNetRuntimePopup.SizeChanged += OnTitleBarExtraSizeChanged;
            PawnIoPopup.SizeChanged += OnTitleBarExtraSizeChanged;
            StatusToggleButton.SizeChanged += OnTitleBarExtraSizeChanged;
            LhmStatusGroup.SizeChanged += OnTitleBarExtraSizeChanged;
            WindowsStatusGroup.SizeChanged += OnTitleBarExtraSizeChanged;

            ApplyStatusGroupOrder();
        }

        private void OnTitleBarExtraSizeChanged(object sender, SizeChangedEventArgs e) => RefreshTitleBarLayout();

        // feeds the room for the status groups; see AppStatusViewModel.UpdateVisibility
        private void AppTitleBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateReadoutRoom();
            this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, RefreshTitleBarLayout);
        }

        // for anything in the bar appearing, disappearing or moving: the passthrough rects of the six interactive
        // elements and the room left for the readout
        // (always at Low priority, after layout; a pill that just became visible measures zero before that)
        private void RefreshTitleBarLayout()
        {
            UpdateReadoutRoom();

            // info popups hand over their button only; (a rect over the readout inside them would block dragging)
            TitleBarPassthrough.Apply(this, AppTitleBar,
                UpdateButton, DotNetRuntimePopup.InteractiveRegion, PawnIoPopup.InteractiveRegion, StatusToggleButton,
                LhmInfoPopup.InteractiveRegion, WindowsInfoPopup.InteractiveRegion);
        }

        // the room between everything in front of the readout (icon, title, update pill, prerequisite hints, toggle)
        // and the caption buttons, and the width each group took the last time it was shown; (all measured, the
        // texts follow the language and text scaling)
        private void UpdateReadoutRoom()
        {
            double inFront = MeasuredWidth(AppTitleIcon) + MeasuredWidth(AppTitleText) + MeasuredWidth(UpdateButton)
                + MeasuredWidth(DotNetRuntimePopup) + MeasuredWidth(PawnIoPopup) + MeasuredWidth(StatusToggleButton);

            double scale = AppTitleBar.XamlRoot?.RasterizationScale ?? 1.0;
            double captionButtons = AppWindow.TitleBar.RightInset / scale;

            if (LhmStatusGroup.Visibility == Visibility.Visible && LhmStatusGroup.ActualWidth > 0)
                _lhmStatusGroupWidth = MeasuredWidth(LhmStatusGroup);
            if (WindowsStatusGroup.Visibility == Visibility.Visible && WindowsStatusGroup.ActualWidth > 0)
                _windowsStatusGroupWidth = MeasuredWidth(WindowsStatusGroup);

            AppStatus.UpdateAvailableWidth(
                AppTitleBar.ActualWidth - inFront - captionButtons - StatusCaptionGap,
                _lhmStatusGroupWidth,
                _windowsStatusGroupWidth);
        }

        // zero until arranged, which is the state on the pass that reveals it; callers come back once it has a size
        private static double MeasuredWidth(FrameworkElement element)
        {
            if (element.Visibility != Visibility.Visible) return 0;

            return element.ActualWidth + element.Margin.Left + element.Margin.Right;
        }

        // a plain Button rather than a ToggleButton; flips the collapsed state itself
        private void StatusToggleButton_Click(object sender, RoutedEventArgs e)
        {
            AppStatus.IsStatusCollapsed = !AppStatus.IsStatusCollapsed;
            this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, RefreshTitleBarLayout);
        }

        // the pill can appear mid-session, so the passthrough rects are recomputed
        private void OnUpdateStateChanged()
        {
            var service = UpdateService.Instance;

            // a store update GitHub could not name yet still needs a label on the pill
            string versionLabel = UpdateService.VersionLabel(service.Latest?.Version ?? "");
            AppStatus.UpdateVersionText = versionLabel.Length > 0 ? versionLabel : AppStrings.Get("Main_UpdatePill");
            AppStatus.IsUpdateAvailable = service.IsUpdateAvailable;

            this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, RefreshTitleBarLayout);
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            var info = UpdateService.Instance.Latest;
            if (info == null) return;

            await UpdateDialog.ShowAsync(((FrameworkElement)this.Content).XamlRoot, info);
        }

        // one of the four status readout settings changed in the settings page
        private void OnStatusReadoutChanged()
        {
            AppStatus.RefreshStatusSettings();
            ApplyStatusGroupOrder();
        }

        // places both status groups in their configured order; (the passthrough rects follow, the popups move and the
        // toggle hides once both groups are off)
        private void ApplyStatusGroupOrder()
        {
            bool lhmFirst = AppStatus.IsLhmGroupFirst;

            Grid.SetColumn(LhmStatusGroup, lhmFirst ? LeadingStatusGroupColumn : TrailingStatusGroupColumn);
            Grid.SetColumn(WindowsStatusGroup, lhmFirst ? TrailingStatusGroupColumn : LeadingStatusGroupColumn);
            LhmStatusGroup.Margin = lhmFirst ? LeadingStatusGroupMargin : TrailingStatusGroupMargin;
            WindowsStatusGroup.Margin = lhmFirst ? TrailingStatusGroupMargin : LeadingStatusGroupMargin;

            this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, RefreshTitleBarLayout);
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

        // reaches XAML only; the tray context menu is a native win32 menu rebuilt by H.NotifyIcon (PopupMenu mode),
        // and 2.4.1 has no theme option for it
        private void ApplyTrayIconTheme(string themeTag)
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

        // where a launch lands, per the settings page; the start page is the default entry point
        private NavigationViewItem StartupNavItem() => SettingsService.Instance.StartupPage switch
        {
            Common.UI.StartupPage.Sensors => SensorsNavItem,
            Common.UI.StartupPage.Performance => PerformanceNavItem,
            _ => StartNavItem
        };

        private void MainNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            // the native settings item
            if (args.IsSettingsSelected)
            {
                return;
            }

            // the page that replaces the splash gets the entrance transition, so the reveal reads as one motion; null
            // leaves every other navigation to the frame default
            NavigationTransitionInfo transition = _isStartupNavigation ? new EntranceNavigationTransitionInfo() : null;

            _isStartupNavigation = false;

            if (args.SelectedItem is NavigationViewItem selectedItem)
            {
                string pageTag = selectedItem.Tag.ToString();
                switch (pageTag)
                {
                    case "Start":
                        contentFrame.Navigate(typeof(StartPage), null, transition);
                        break;

                    case "Sensors":
                        contentFrame.Navigate(typeof(SensorsPage), null, transition);
                        break;

                    case "Settings":
                        contentFrame.Navigate(typeof(SettingsPage), null, transition);
                        break;

                    case "Performance":
                        contentFrame.Navigate(typeof(PerformancePage), null, transition);
                        break;
                }
            }
        }


        // === window state and system tray ===

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            // a forced shutdown (settings reset or import, then restart) must not overwrite what was just saved
            if (_isForceClosing) return;

            if (args.DidPresenterChange)
            {
                CheckAndHideToTray();
            }
            if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange)
            {
                SaveWindowState();
            }

            // minimize and restore only show up as DidSizeChange, hide and show as DidVisibilityChange; (after
            // CheckAndHideToTray, so a Hide() it just did is already in IsVisible)
            if (args.DidSizeChange || args.DidVisibilityChange)
            {
                UpdatePerformancePageRenderingState();
            }
        }

        // pauses the performance page graphs while this window is minimized or hidden; (a no-op on any other page)
        private void UpdatePerformancePageRenderingState()
        {
            if (contentFrame.Content is not PerformancePage performancePage) return;

            bool isMinimized = this.AppWindow.Presenter is OverlappedPresenter presenter &&
                               presenter.State == OverlappedPresenterState.Minimized;

            performancePage.SetWindowVisibilityActive(this.AppWindow.IsVisible && !isMinimized);
        }

        public void CheckAndHideToTray()
        {
            if (!SettingsService.Instance.MinimizeToTray) return;

            // main window: closed, hidden or minimized
            bool isMainReady = _isDashboardClosed || !this.AppWindow.IsVisible ||
                               (this.AppWindow.Presenter is OverlappedPresenter opMain && opMain.State == OverlappedPresenterState.Minimized);

            // widget windows: absent, hidden or minimized
            bool isWidgetReady = WidgetWindow.OpenInstances.All(widget =>
                !widget.AppWindow.IsVisible ||
                (widget.AppWindow.Presenter is OverlappedPresenter opWidget && opWidget.State == OverlappedPresenterState.Minimized));

            // all out of the way: hide the app from the taskbar
            if (isMainReady && isWidgetReady)
            {
                // unless the hide shield already has it
                if (!_isDashboardClosed)
                {
                    this.Hide();
                }

                foreach (var widget in WidgetWindow.OpenInstances)
                {
                    widget.Hide();
                }
            }
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // QuitAppNow or ForceExit is ending the process
            if (_isForceClosing) return;

            if (SettingsService.Instance.MinimizeToTray)
            {
                args.Cancel = true;

                HideToTray();
                CheckAndHideToTray();
            }
            else
            {
                // MinimizeToTray off: closing the main window exits the app, whatever else is open; (a retained widget
                // window cancels its own close, so only a hard kill ends the process)
                QuitAppNow();
            }
        }

        // closing with MinimizeToTray on and starting with StartMinimizedToTray on; (sets _isDashboardClosed, which
        // the tray restore path checks)
        private void HideToTray()
        {
            _isDashboardClosed = true;

            ApplyHideShield();
            this.Hide();
        }

        private void ApplyHideShield()
        {
            // see the workaround on the P/Invoke declarations
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        // only a launch by the autostart task starts hidden (it marks its launches with an argument); opening the app
        // yourself always shows the window
        private static bool StartsHiddenInTray() =>
            SettingsService.Instance.StartMinimizedToTray && WinAutostartService.StartedByTask;

        public void OpenDashboard()
        {
            _isDashboardClosed = false;

            // lift the hide shield
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TOOLWINDOW & ~WS_EX_NOACTIVATE);

            this.Show();
            if (this.AppWindow.Presenter is OverlappedPresenter opMain)
            {
                opMain.Restore();
            }
            this.Activate();
            SetForegroundWindow(hwnd); // see the workaround on the P/Invoke
        }

        // the sensor list with a profile preselected, for the taskbar flyout bar action; not via RestoreApp, which
        // stays silent after a close with X and brings the widget window back too
        public void OpenSensorsForProfile(SensorSelectionProfile profile)
        {
            OpenDashboard();

            // SelectionChanged fires only on a real change; the profile below applies either way
            var sensorsItem = SensorsNavItem;
            if (!ReferenceEquals(MainNavigationView.SelectedItem, sensorsItem))
            {
                MainNavigationView.SelectedItem = sensorsItem;
            }

            if (contentFrame.Content is SensorsPage sensorsPage)
            {
                sensorsPage.SelectProfile(profile);
            }
            else
            {
                // splash is still running, StartHardwareServiceAsync picks this up once it selects the page
                _pendingSensorProfile = profile;
            }
        }

        // the sensor list with one hardware group open and the rest closed, for the start page snapshot tiles
        public void OpenSensorsForHardware(IReadOnlyList<string> lhmHardwareNames)
        {
            if (lhmHardwareNames == null || lhmHardwareNames.Count == 0) return;

            OpenDashboard();

            if (!ReferenceEquals(MainNavigationView.SelectedItem, SensorsNavItem))
            {
                MainNavigationView.SelectedItem = SensorsNavItem;
            }

            if (contentFrame.Content is SensorsPage sensorsPage)
            {
                sensorsPage.ExpandHardwareGroup(lhmHardwareNames);
            }
            else
            {
                _pendingSensorHardware = lhmHardwareNames;
            }
        }

        // the settings page on its taskbar section, for the gear button of the taskbar flyout
        public void OpenTaskbarSettings()
        {
            OpenDashboard();

            if (!ReferenceEquals(MainNavigationView.SelectedItem, SettingsNavItem))
            {
                MainNavigationView.SelectedItem = SettingsNavItem;
            }

            if (contentFrame.Content is SettingsPage settingsPage)
            {
                settingsPage.ScrollToTaskbarSection();
            }
            else
            {
                _isTaskbarSettingsPending = true;
            }
        }

        private void RestoreApp()
        {
            // the tray menu entries; the main window only if it was not closed with X
            if (!_isDashboardClosed)
            {
                OpenDashboard();
            }

            // every widget window that exists
            foreach (var widget in WidgetWindow.OpenInstances)
            {
                widget.Show();
                if (widget.AppWindow.Presenter is OverlappedPresenter opWidget)
                {
                    opWidget.Restore();
                }
                widget.Activate();
            }
        }

        // one tray entry per open widget window, in window order at the top of the menu; H.NotifyIcon builds its native
        // menu from Items on every open and ignores Visibility, so the entries go in and out of the list instead
        private void UpdateWidgetTrayItems()
        {
            foreach (var entry in _widgetTrayItems)
            {
                TrayMenu.Items.Remove(entry);
            }

            int insertAt = 0;
            for (int index = 0; index < _widgetTrayItems.Length; index++)
            {
                if (WidgetWindow.GetOpenInstance(index) != null)
                {
                    TrayMenu.Items.Insert(insertAt++, _widgetTrayItems[index]);
                }
            }
        }

        // opens the project page in the browser; (the tray menu reaches it without the main window open)
        private static void OpenProjectPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo(ProjectPageUrl) { UseShellExecute = true });
            }
            catch { /* no browser reachable, and a tray menu has nowhere to report that to */ }
        }

        // hard kill instead of the WinUI Closing/Exit path, for tray Exit and a close with MinimizeToTray off;
        // (Application.Current.Exit() is unreliable with several windows open)
        private void QuitAppNow()
        {
            _isForceClosing = true;
            SaveWindowState();
            PersistenceService.Instance.FlushAll();
            Process.GetCurrentProcess().Kill();
        }

        // controlled tear-down for paths that bypass the normal closing (a settings reset or import restart)

        // --- workaround: second instance survives an automatic restart ---
        // problem: Application.Current.Exit() does not reliably end the process while no window is active; the
        // settings import restart hits that state and left two full instances running:
        // https://github.com/microsoft/microsoft-ui-xaml/issues/5931
        // fix: hard kill instead of Exit(), on this restart path only
        public void ForceExit()
        {
            _isForceClosing = true;

            // --- workaround: Kill() never reached ---
            // problem: HardwareMonitorService.Cleanup() -> Computer.Close() can hang indefinitely while the restarted
            // process races for the same kernel driver handle; no public issue found
            // fix: skip Cleanup() on this path, the OS releases the driver handle once the process is gone
            // FlushAll before Kill() is the last point state can reach disk; no SaveWindowState(), so a window state
            // reset is not overwritten on the way out
            PersistenceService.Instance.FlushAll();
            Process.GetCurrentProcess().Kill();
        }

        // writes position and size (debounced); skipped while minimized or hidden, those rects are transient
        private void SaveWindowState()
        {
            var presenter = this.AppWindow.Presenter as OverlappedPresenter;
            bool isMinimized = presenter != null && presenter.State == OverlappedPresenterState.Minimized;
            if (isMinimized || !this.AppWindow.IsVisible) return;

            bool isMaximized = presenter != null && presenter.State == OverlappedPresenterState.Maximized;

            // while maximized the restored rect is kept, so un-maximizing returns to it
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
