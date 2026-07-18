using FluentHwInfo.Services;
using FluentHwInfo.ViewModels;
using FluentHwInfo.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.ApplicationSettings;
using WinUIEx;
using WindowState = FluentHwInfo.Models.WindowState;

namespace FluentHwInfo
{
    public sealed partial class MainWindow : Window
    {
        // win32 api imports
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


        // properties and fields
        public static MainWindow CurrentInstance { get; private set; }
        private const string WindowKey = "Main"; // key under which this windows state is saved
        private bool _isForceClosing = false;
        private bool _isHardwareServiceLoaded = false;
        private bool _isDashboardClosed = false;
        public XamlUICommand RestoreAppCommand { get; } = new XamlUICommand(); // restore
        public XamlUICommand ShowMainWindowCommand { get; } = new XamlUICommand(); // restore + navigate to SensorPage
        public XamlUICommand OpenSettingsCommand { get; } = new XamlUICommand(); // restore + navigate to SettingsPage
        public XamlUICommand ExitAppCommand { get; } = new XamlUICommand();


        // constructor
        public MainWindow()
        {
            // initialization
            this.InitializeComponent();
            this.AppWindow.SetIcon("Assets\\Icon\\Icon.ico");
            CurrentInstance = this;

            // AppWindow configuration
            // theme 
            AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            if (AppWindow.TitleBar.ExtendsContentIntoTitleBar)
            {
                AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
                AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

            }
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
                this.SetWindowSize(690, 650);
                this.CenterOnScreen();
                var currentPos = this.AppWindow.Position;
                // yea idk; might change this in future
                this.AppWindow.Move(new Windows.Graphics.PointInt32(currentPos.X - 400, currentPos.Y - 100));
             }

            // theming
            FluentHwInfo.Services.SettingsService.Instance.ThemeChanged += OnThemeChanged;
            ApplyTitleBarTheme(FluentHwInfo.Services.SettingsService.Instance.AppTheme);
            ApplyTrayIconTheme(FluentHwInfo.Services.SettingsService.Instance.AppTheme);
            ApplyTheme(FluentHwInfo.Services.SettingsService.Instance.AppTheme);

            // event routing
            this.Closed += (s, args) =>
            {
                FluentHwInfo.Services.SettingsService.Instance.ThemeChanged -= OnThemeChanged;
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
            OpenSettingsCommand.ExecuteRequested += (s, e) =>
            {
                RestoreApp();
                MainNavigationView.SelectedItem = MainNavigationView.FooterMenuItems[0]; 
            };
            ExitAppCommand.ExecuteRequested += (s, e) =>
            {
                _isForceClosing = true;
                SaveWindowState();

                // same as EvaluateFullExit: stop the polling loop before tearing down the UI
                FluentHwInfo.Services.HardwareMonitorService.Instance.StopMonitoring();

                PersistenceService.Instance.FlushAll();
                Application.Current.Exit();
            };
        }


        // lifecycle and initialization methods
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isHardwareServiceLoaded) return;
            _isHardwareServiceLoaded = true;

            await StartHardwareServiceAsync(); // load the HardwareMonitorService singleton instance asynchronously
        }
        private async Task StartHardwareServiceAsync()
        {
            var monitor = FluentHwInfo.Services.HardwareMonitorService.Instance;

            // scan motherboard
            LoadingStatusText.Text = "Initializing motherboard...";
            LoadingProgressBar.Value = 25;
            await monitor.InitMotherboardAsync();
            // await Task.Delay(4000000);

            // scan CPU
            LoadingStatusText.Text = "Scanning CPU...";
            LoadingProgressBar.Value = 50;
            await monitor.InitCpuAsync();

            // scan GPU
            LoadingStatusText.Text = "Scanning GPU...";
            LoadingProgressBar.Value = 75;
            await monitor.InitGpuAsync();

            // scan memory and storage
            LoadingStatusText.Text = "Checking memory and storage...";
            LoadingProgressBar.Value = 100;
            await monitor.InitMemoryAndStorageAsync();

            // no we start the HardwareMonitorService loop manually
            monitor.StartMonitoring();

            // we explicitly wait until the ViewModel has received and processed the very first data payload
            LoadingStatusText.Text = "Waiting for data...";
            await FluentHwInfo.ViewModels.SensorsViewModel.Instance.WaitForInitialLoadAsync();

            // now we are finished loading
            LoadingStatusText.Text = "Ready";
            await Task.Delay(500);

            // show the main grid
            MainNavigationView.Visibility = Visibility.Visible;

            // manually close navigation pane
            this.DispatcherQueue.TryEnqueue(() =>
            {
                MainNavigationView.IsPaneOpen = false;
            });
            await Task.Delay(200);

            SplashOverlay.Visibility = Visibility.Collapsed;
            MainNavigationView.SelectedItem = MainNavigationView.MenuItems[0];

            // re-open the widget window with its previously pinned sensors, if it was still open when the app last closed
            TryRestoreWidgetWindow();
        }


        // theme change handling
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


        // navigation
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
                string pageTag = selectedItem.Tag.ToString(); // we pull the value of the tag of the selected item
                switch (pageTag)
                {
                    case "Sensors":
                        // typeof() specifies the class that the frame should load
                        contentFrame.Navigate(typeof(SensorsPage));
                        break;

                    case "Settings":
                        contentFrame.Navigate(typeof(SettingsPage)); 
                        break;
                }
            }
        }


        // windows state and system tray logic
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
            if (Views.WidgetWindow.CurrentInstance != null)
            {
                var opWidget = Views.WidgetWindow.CurrentInstance.AppWindow.Presenter as OverlappedPresenter;
                isWidgetReady = !Views.WidgetWindow.CurrentInstance.AppWindow.IsVisible || (opWidget != null && opWidget.State == OverlappedPresenterState.Minimized);
            }

            // if both windows are out of the way, hide the app completely from the taskbar
            if (isMainReady && isWidgetReady)
            {
                // only call hide if it's not already locked down by the Win32 closing shield
                if (!_isDashboardClosed)
                {
                    this.Hide();
                }

                if (Views.WidgetWindow.CurrentInstance != null)
                {
                    Views.WidgetWindow.CurrentInstance.Hide();
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

                // Win32 iron shield; makes the window invisible to the OS window manager
                // WS_EX_TOOLWINDOW hides it from Alt+Tab
                // WS_EX_NOACTIVATE prevents Windows from auto-focusing it
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

                this.Hide();
                CheckAndHideToTray();
                EvaluateFullExit();
            }
            else
            {
                FluentHwInfo.Services.HardwareMonitorService.Instance.StopMonitoring();

                // MinimizeToTray is off: the window is actually about to close for real, capture its final rect and
                // write everything to disk before the process ends
                SaveWindowState();
                PersistenceService.Instance.FlushAll();
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
            if (Views.WidgetWindow.CurrentInstance != null)
            {
                Views.WidgetWindow.CurrentInstance.Show();
                if (Views.WidgetWindow.CurrentInstance.AppWindow.Presenter is OverlappedPresenter opWidget)
                {
                    opWidget.Restore();
                }
                Views.WidgetWindow.CurrentInstance.Activate();
            }
        }
        public void EvaluateFullExit()
        {
            // if the dashboard is closed and no widget is pinned anymore, nothing is left running;
            // fully exit instead of sitting in the tray forever with no way to bring it back
            if (_isDashboardClosed && Views.WidgetWindow.CurrentInstance == null)
            {
                _isForceClosing = true;

                // stop the background polling loop first, so no more sensor updates can hit UI elements while the XAML
                // tree is being torn down below
                FluentHwInfo.Services.HardwareMonitorService.Instance.StopMonitoring();

                PersistenceService.Instance.FlushAll();
                Application.Current.Exit();
            }
        }
        // controlled tear-down for scenarios that bypass the normal closing paths (e.g. settings reset -> app restart);
        // mirrors ExitAppCommands sequence but deliberately skips SaveWindowState so a fresh window-state reset does not
        // get immediately overwritten by a final position save on the way out
        public void ForceExit()
        {
            _isForceClosing = true;

            // deliberately no HardwareMonitorService.Cleanup() here anymore:
            // Computer.Close() can wedge a background thread inside a blocking WinRing0 driver call, which TerminateProcess
            // then cannot fully tear down
            // that was the actual cause of the unreliable auto-restart, not something upstream of Kill() itself. flush persisted
            // state (plain file I/O, no driver involved) and hard-kill; any stray driver handle gets released by the OS once the
            // process is gone
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
            var existing = WindowStateService.Instance.GetState(WindowKey) ?? new WindowState();
            var newState = new WindowState
            {
                X = isMaximized ? existing.X : this.AppWindow.Position.X,
                Y = isMaximized ? existing.Y : this.AppWindow.Position.Y,
                Width = isMaximized ? existing.Width : this.AppWindow.Size.Width,
                Height = isMaximized ? existing.Height : this.AppWindow.Size.Height,
                IsMaximized = isMaximized
            };

            WindowStateService.Instance.SetState(WindowKey, newState);
        }

        // re-creates the widget window with whichever previously pinned sensors still exist on
        // this system, but only if it was actually open when the app last closed
        private void TryRestoreWidgetWindow()
        {
            var widgetState = WindowStateService.Instance.GetState("Widget");
            if (widgetState == null || !widgetState.WasOpen || widgetState.PinnedSensorIds.Count == 0) return;

            var pinnedSensors = FindSensorRowsByIds(widgetState.PinnedSensorIds);
            if (pinnedSensors.Count == 0) return; // none of them exist on this system anymore

            var widgetWindow = new Views.WidgetWindow(pinnedSensors);
            widgetWindow.Activate();
        }

        // looks up live SensorRowViewModel instances (visible or hidden) by their saved IDs, preserving the original
        // pin order rather than whatever order the hardware groups produce
        private List<SensorRowViewModel> FindSensorRowsByIds(List<string> ids)
        {
            var allSensors = SensorsViewModel.Instance.HardwareGroups
                .SelectMany(g => g.Sensors.Concat(g.HiddenSensors));

            return ids
                .Select(id => allSensors.FirstOrDefault(s => s.Id == id))
                .Where(s => s != null)
                .ToList();
        }
    }
}
