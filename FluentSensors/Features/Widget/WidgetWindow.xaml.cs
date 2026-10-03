using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using WinRT;
using System.Runtime.InteropServices;
using System.Linq;
using WinUIEx;

using FluentSensors.Persistence.Services;
using FluentSensors.Persistence.Models;
using FluentSensors.Controls.SensorRow;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Controls.TimeRange;
using FluentSensors.Features.Sensors;
using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;
using FluentSensors.Features.TaskbarWidget;


namespace FluentSensors.Features.Widget
{
    public sealed partial class WidgetWindow : Window
    {
        // === win32 api imports ===

        // screen scaling
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);


        // === fields ===

        private AppWindow _appWindow;
        private const string WindowKey = "Widget";

        // resize floor in DIP; MinPanelHeight per pinned sensor, MinWidgetWidth for the window
        private const int MinPanelHeight = 90;
        private const int MinWidgetWidth = 220;

        // in DIP; title bar 26, graph bottom padding 5, bottom bar 31 (divider, padding, 22 buttons)
        private const int ChromeHeight = 62;

        public WidgetViewModel ViewModel { get; }
        public static WidgetWindow CurrentInstance { get; private set; }
        public static event Action WidgetStateChanged;
        private static WidgetWindow _retainedInstance;

        // system backdrop
        private DesktopAcrylicController _acrylicController;
        private MicaController _micaController;
        private SystemBackdropConfiguration _configurationSource;
        private Windows.UI.ViewManagement.UISettings? _uiSettings;


        // === constructor ===

        public WidgetWindow(List<SensorRowViewModel> selectedSensors)
        {
            ViewModel = new WidgetViewModel(selectedSensors);
            this.InitializeComponent();
            this.AppWindow.SetIcon("Assets\\Icon\\Icon.ico");
            CurrentInstance = this;
            WidgetStateChanged?.Invoke();

            // a click on empty space hides the keyboard focus rectangle again
            PointerFocusReset.Attach(Content);

            // window configuration
            _appWindow = this.AppWindow;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomTitleBar);

            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = true; // readable over other apps
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
            presenter.IsResizable = true;
            _appWindow.SetPresenter(presenter);

            // resize floor for the pinned sensor count; (ReconfigureFor recalculates it)
            ApplyMinimumWindowSize(selectedSensors.Count);

            // restores X, Y and width when they land on a connected monitor, otherwise only sizes the window and
            // Windows places it; the height always follows the current sensor count
            double scaleFactor = GetScaleFactor();
            var savedState = WindowStateService.Instance.GetState(WindowKey);
            if (savedState != null && IsPositionOnScreen(savedState.X, savedState.Y, savedState.Width, savedState.Height))
            {
                int height = CalculateWidgetHeight(selectedSensors.Count, scaleFactor);
                _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                    savedState.X, savedState.Y, savedState.Width, height));
            }
            else
            {
                ResizeWidgetToFitSensors(selectedSensors.Count);
            }

            // marked open, so it reopens on the next launch (with the sensors from SensorSelectionService)
            SaveWindowState();

            // theming
            SetBackdrop(SettingsService.Instance.BackdropType);
            ApplyTheme(SettingsService.Instance.AppTheme);

            // event routing
            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            SettingsService.Instance.BackdropTypeChanged += OnBackdropTypeChanged;
            SettingsService.Instance.OpacityChanged += OnOpacityChanged;
            SettingsService.Instance.TintColorChanged += OnTintColorChanged;

            // the widget time range; the picker and the settings page write the same setting
            TimeRangePicker.SelectedSeconds = SettingsService.Instance.GraphTimeSpanSeconds;
            TimeRangePicker.RegisterPropertyChangedCallback(TimeRangePickerControl.SelectedSecondsProperty, OnTimeRangePicked);
            SettingsService.Instance.GraphTimeSpanChanged += OnGraphTimeSpanChanged;

            ApplyPauseState();

            try
            {
                _uiSettings = new Windows.UI.ViewManagement.UISettings();
                TaskbarFlyoutWindow.SeedSystemVisualsSnapshot(_uiSettings);
                _uiSettings.AdvancedEffectsEnabledChanged += OnSystemVisualSettingsChanged;
                _uiSettings.ColorValuesChanged += OnSystemVisualSettingsChanged;
            }
            catch { }

            this.Closed += WidgetWindow_Closed;
            _appWindow.Changed += AppWindow_Changed;
            _appWindow.Closing += AppWindow_Closing;

            KickBackdropRefresh();
        }


        // === public methods ===

        // shows the widget, reusing the hidden _retainedInstance if there is one
        public static void ShowWithSensors(List<SensorRowViewModel> selectedSensors)
        {
            // already open: swap the content and resize in place
            if (CurrentInstance != null)
            {
                CurrentInstance.ReconfigureFor(selectedSensors);
                CurrentInstance.Activate();
                return;
            }

            // hidden by its X: reuse that window
            if (_retainedInstance != null)
            {
                var window = _retainedInstance;
                _retainedInstance = null;

                window.ReconfigureFor(selectedSensors);
                CurrentInstance = window;
                WidgetStateChanged?.Invoke();

                // level 2 reverse: flat baseline, live data, then rendering; the reopened widget starts fresh
                window.ViewModel.SetLiveDataActive(true);
                window.SetGraphsRenderingActive(true);
                window.ApplyPauseState(); // the close ended the snapshot

                window._appWindow.Show();
                window.Activate();
                return;
            }

            // none yet this session
            var newWindow = new WidgetWindow(selectedSensors);
            newWindow.Activate();
        }

        // brings the widget back without touching its content, for the tray single click; a no-op unless it is open
        public static void RestoreIfOpen()
        {
            if (CurrentInstance == null) return;

            if (CurrentInstance._appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Restore();
            }
            CurrentInstance._appWindow.Show();
            CurrentInstance.Activate();
        }

        private bool _isClosed = false;

        public void SafeDestroy()
        {
            if (_isClosed) return;
            _isClosed = true;

            try
            {
                SettingsService.Instance.ThemeChanged -= OnThemeChanged;
                SettingsService.Instance.BackdropTypeChanged -= OnBackdropTypeChanged;
                SettingsService.Instance.OpacityChanged -= OnOpacityChanged;
                SettingsService.Instance.TintColorChanged -= OnTintColorChanged;
                SettingsService.Instance.GraphTimeSpanChanged -= OnGraphTimeSpanChanged;
            }
            catch { }

            try
            {
                if (_uiSettings != null)
                {
                    _uiSettings.AdvancedEffectsEnabledChanged -= OnSystemVisualSettingsChanged;
                    _uiSettings.ColorValuesChanged -= OnSystemVisualSettingsChanged;
                    _uiSettings = null;
                }
            }
            catch { }

            // detached first, or AppWindow_Closing cancels the Close below and brings this window back as
            // _retainedInstance; WidgetWindow_Closed stays, it does the real teardown
            try
            {
                _appWindow.Closing -= AppWindow_Closing;
                _appWindow.Changed -= AppWindow_Changed;
            }
            catch { }

            try
            {
                _acrylicController?.Dispose();
                _acrylicController = null;
                _micaController?.Dispose();
                _micaController = null;
                this.Close();
            }
            catch { }
        }

        private static bool _isRecreating = false;

        // destroys and rebuilds the widget on an OS theme or transparency change
        public static void RecreateWindow()
        {
            if (_isRecreating) return;
            if (CurrentInstance == null && _retainedInstance == null) return;
            _isRecreating = true;

            var ids = SensorSelectionService.Instance.GetSelection(SensorSelectionProfile.WidgetWindow);
            var sensors = ResolveSensors(ids);
            bool wasVisible = CurrentInstance != null && CurrentInstance._appWindow != null && CurrentInstance._appWindow.IsVisible;

            if (CurrentInstance != null)
            {
                var old = CurrentInstance;
                CurrentInstance = null;
                old.SafeDestroy();
            }

            if (_retainedInstance != null)
            {
                var old = _retainedInstance;
                _retainedInstance = null;
                old.SafeDestroy();
            }

            if (sensors.Count > 0 && wasVisible)
            {
                // a fallback queue for a null MainWindow.CurrentInstance; (skipping the finally would latch
                // _isRecreating and the widget would never rebuild again)
                var queue = MainWindow.CurrentInstance?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
                bool queued = queue != null && queue.TryEnqueue(() =>
                {
                    try
                    {
                        ShowWithSensors(sensors);
                    }
                    finally
                    {
                        _isRecreating = false;
                    }
                });

                if (!queued)
                {
                    _isRecreating = false;
                }
            }
            else
            {
                _isRecreating = false;
            }
        }

        private static List<SensorRowViewModel> ResolveSensors(IReadOnlyList<string> sensorIds)
        {
            if (sensorIds == null || sensorIds.Count == 0 || SensorsViewModel.Instance == null)
            {
                return new List<SensorRowViewModel>();
            }

            // walks the groups, not the id list, so the order matches a live pin; (the saved list is in toggle order)
            var wantedIds = new HashSet<string>(sensorIds);

            return SensorsViewModel.Instance.HardwareGroups
                .SelectMany(g => g.Sensors.Concat(g.HiddenSensors))
                .Where(sensor => wantedIds.Contains(sensor.Id))
                .ToList();
        }


        // === lifecycle ===

        private void WidgetWindow_Closed(object sender, WindowEventArgs args)
        {
            // marked closed, so it does not reopen on the next launch; the pinned selection stays
            // with SensorSelectionService
            SaveWindowState(wasOpen: false);

            // settings events
            SettingsService.Instance.BackdropTypeChanged -= OnBackdropTypeChanged;
            SettingsService.Instance.OpacityChanged -= OnOpacityChanged;
            SettingsService.Instance.TintColorChanged -= OnTintColorChanged;
            SettingsService.Instance.ThemeChanged -= OnThemeChanged;
            SettingsService.Instance.GraphTimeSpanChanged -= OnGraphTimeSpanChanged;

            // the HardwareMonitorService subscription
            ViewModel.Cleanup();

            // backdrop controllers, per the Microsoft docs
            _acrylicController?.Dispose();
            _acrylicController = null;
            _micaController?.Dispose();
            _micaController = null;

            this.Activated -= Window_Activated;
            _configurationSource = null;
            CurrentInstance = null;
            WidgetStateChanged?.Invoke();
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_configurationSource != null)
            {
                // always active, or a click outside drops the blur while the widget stays on screen
                _configurationSource.IsInputActive = true;
            }
        }

        // --- memory leak: WidgetWindow never released after close ---
        // problem: WinUI 3 never releases a closed secondary Window (confirmed, still open, even with empty content):
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // fix: hide instead of closing and keep the instance as _retainedInstance (same as HiddenSensorsWindow);
        // controllers, settings events and the ViewModel stay alive for the reuse
        // always, whatever MinimizeToTray says; quitting is decided by MainWindow and the tray Exit, never here
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // SafeDestroy is closing for real; a closed window in _retainedInstance would ignore every
            // later SetBackdrop and ApplyTheme
            if (_isClosed) return;

            args.Cancel = true;

            SaveWindowState(wasOpen: false);
            CurrentInstance = null;
            _retainedInstance = this;
            WidgetStateChanged?.Invoke();

            _appWindow.Hide();

            // level 2: a closed widget does nothing in the background; rendering stops first, so the
            // history wipe fires no repaints
            SetGraphsRenderingActive(false);
            ViewModel.SetLiveDataActive(false);
        }


        // === user interaction ===

        private void OnTimeRangePicked(DependencyObject sender, DependencyProperty dp)
        {
            SettingsService.Instance.GraphTimeSpanSeconds = TimeRangePicker.SelectedSeconds;
        }

        // the snapshot; every graph stands still, like the taskbar flyout
        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SetPaused(!ViewModel.IsPaused);
            ApplyPauseState();
        }

        // glyph, tooltip and name follow the state, like the taskbar flyout pause button
        private void ApplyPauseState()
        {
            string label = ViewModel.IsPaused ? "Resume" : "Pause";
            PauseButtonIcon.Glyph = ViewModel.IsPaused ? "\uE768" : "\uE769";
            ToolTipService.SetToolTip(PauseButton, label);
            AutomationProperties.SetName(PauseButton, label);
        }

        private void BackToDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.CurrentInstance != null)
            {
                MainWindow.CurrentInstance.OpenDashboard();
            }
            else
            {
                // no main window left; the open widget keeps the process alive
                var newMainWindow = new MainWindow();
                newMainWindow.Activate();
            }
        }


        // === settings event listeners and handlers ===

        private void OnThemeChanged(string newTheme)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                ApplyTheme(newTheme);
            });
        }

        private void OnBackdropTypeChanged(string newType)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                SetBackdrop(newType);
            });
        }

        private void OnOpacityChanged(float tintOpacity, float luminosityOpacity)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                UpdateAcrylicProperties();
            });
        }

        private void OnTintColorChanged(bool useAccentColor, Windows.UI.Color customColor)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                UpdateAcrylicProperties();
                UpdateSolidBackground();
            });
        }

        private void OnGraphTimeSpanChanged(double newTimeSpanSeconds)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                TimeRangePicker.SelectedSeconds = newTimeSpanSeconds;
            });
        }

        // named so SafeDestroy can detach it (every rebuilt window subscribes anew); goes through the router, so a pure
        // accent change refreshes in place
        private void OnSystemVisualSettingsChanged(Windows.UI.ViewManagement.UISettings sender, object args)
        {
            TaskbarFlyoutWindow.RouteSystemVisualsChange(sender);
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            // --- workaround: minimize/restore never gated, DidPresenterChange does not cover it ---
            // problem: DidPresenterChange only fires when the Presenter is swapped; minimize, maximize and restore are
            // a state change within the same OverlappedPresenter and never set it:
            // https://learn.microsoft.com/en-us/windows/apps/develop/ui/manage-app-windows
            // fix: they show up as DidSizeChange (as in the OverlappedPresenterState sample), which
            // reads presenter.State below
            if (args.DidPresenterChange && MainWindow.CurrentInstance != null)
            {
                // the main window re-evaluates the tray state
                MainWindow.CurrentInstance.CheckAndHideToTray();
            }

            if (args.DidSizeChange)
            {
                // level 1: minimized graphs stop drawing but keep filling, so a restore shows the continuous
                // history (a close resets instead)
                bool isMinimized = sender.Presenter is OverlappedPresenter presenter &&
                                   presenter.State == OverlappedPresenterState.Minimized;
                SetGraphsRenderingActive(!isMinimized);
            }

            if ((args.DidPositionChange || args.DidSizeChange) && this.AppWindow.IsVisible)
            {
                SaveWindowState();
            }
        }

        // level 1 gate: rendering only, the data lists keep filling either way
        private void SetGraphsRenderingActive(bool active)
        {
            if (this.Content is DependencyObject root)
            {
                SensorGraphRenderingGate.SetActive(root, active);
            }
        }


        // === window sizing and positioning ===

        private double GetScaleFactor()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            return dpi / 96.0; // 96 DPI = 100%
        }

        // physical height for the pinned sensor count
        private int CalculateWidgetHeight(int sensorCount, double scaleFactor)
        {
            // title bar and bottom bar + n * (panel + spacing)
            double desiredXamlHeight = ChromeHeight + (sensorCount * (AppSettingsData.WidgetWindowDefaultPanelHeightDip + 8));
            int physicalHeight = (int)(desiredXamlHeight * scaleFactor);

            int screenHeight = DisplayArea.Primary.WorkArea.Height;
            return Math.Min(physicalHeight, screenHeight - 40); // capped at the screen
        }

        // the resize floor from MinWidgetWidth and MinPanelHeight
        private void ApplyMinimumWindowSize(int sensorCount)
        {
            var manager = WindowManager.Get(this);
            manager.MinWidth = MinWidgetWidth;
            manager.MinHeight = CalculateWidgetMinHeight(sensorCount, GetScaleFactor());
        }

        // CalculateWidgetHeight for the floor, with MinPanelHeight
        private double CalculateWidgetMinHeight(int sensorCount, double scaleFactor)
        {
            double minXamlHeight = ChromeHeight + (sensorCount * (MinPanelHeight + 8)); // title and bottom bar + n * (min panel + spacing)

            double screenHeightDip = DisplayArea.Primary.WorkArea.Height / scaleFactor;
            return Math.Min(minXamlHeight, screenHeightDip - 40); // capped at the screen
        }

        // whether the rect is on a connected monitor; (a saved position goes stale when its monitor is gone)
        private bool IsPositionOnScreen(int x, int y, int width, int height)
        {
            var rect = new Windows.Graphics.RectInt32(x, y, width, height);

            // indexed loop; foreach over DisplayArea.FindAll() throws an InvalidCastException (WinRT enumerator bug)
            var displayAreas = DisplayArea.FindAll();
            for (int i = 0; i < displayAreas.Count; i++)
            {
                if (RectsOverlap(rect, displayAreas[i].WorkArea))
                {
                    return true;
                }
            }
            return false;
        }

        private bool RectsOverlap(Windows.Graphics.RectInt32 a, Windows.Graphics.RectInt32 b)
        {
            return a.X < b.X + b.Width && a.X + a.Width > b.X &&
                   a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
        }

        // sizes for the pinned sensor count without a position; the fallback without a valid saved one
        // (first launch, monitor gone)
        private void ResizeWidgetToFitSensors(int sensorCount)
        {
            double scaleFactor = GetScaleFactor();
            int physicalHeight = CalculateWidgetHeight(sensorCount, scaleFactor);

            double desiredXamlWidth = AppSettingsData.WidgetWindowDefaultWidthDip;
            int physicalWidth = (int)(desiredXamlWidth * scaleFactor);

            _appWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));
        }

        // unused; kept for a pin-to-corner action
        private void PositionWidgetTopRight(int sensorCount)
        {
            double scaleFactor = GetScaleFactor();

            // work area, physical px
            var displayArea = DisplayArea.Primary;
            int screenWidth = displayArea.WorkArea.Width;

            // default width, to physical px
            double desiredXamlWidth = AppSettingsData.WidgetWindowDefaultWidthDip;
            int physicalWidth = (int)(desiredXamlWidth * scaleFactor);
            int physicalHeight = CalculateWidgetHeight(sensorCount, scaleFactor);

            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                screenWidth - physicalWidth - 10, // 10px from the right edge
                10, // 10px from the top edge
                physicalWidth,
                physicalHeight));
        }

        // a new sensor set in the existing window: content, floor and size
        private void ReconfigureFor(List<SensorRowViewModel> selectedSensors)
        {
            ViewModel.Reconfigure(selectedSensors);
            ApplyMinimumWindowSize(selectedSensors.Count);

            double scaleFactor = GetScaleFactor();
            var savedState = WindowStateService.Instance.GetState(WindowKey);
            if (savedState != null && IsPositionOnScreen(savedState.X, savedState.Y, savedState.Width, savedState.Height))
            {
                int height = CalculateWidgetHeight(selectedSensors.Count, scaleFactor);
                _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                    savedState.X, savedState.Y, savedState.Width, height));
            }
            else
            {
                ResizeWidgetToFitSensors(selectedSensors.Count);
            }

            SaveWindowState();
        }

        // writes rect and open state (debounced); the pinned sensors belong to SensorSelectionService
        private void SaveWindowState(bool wasOpen = true)
        {
            var state = WindowStateService.Instance.GetState(WindowKey) ?? new Persistence.Models.WindowState();

            // a minimized window reports (-32000, -32000); the last real rect is kept
            bool isMinimized = this.AppWindow.Presenter is OverlappedPresenter presenter &&
                                presenter.State == OverlappedPresenterState.Minimized;
            if (!isMinimized)
            {
                state.X = _appWindow.Position.X;
                state.Y = _appWindow.Position.Y;
                state.Width = _appWindow.Size.Width;
                state.Height = _appWindow.Size.Height;
            }
            state.WasOpen = wasOpen;

            WindowStateService.Instance.SetState(WindowKey, state);
        }


        // === theme and backdrop application ===

        private void ApplyTheme(string themeTag)
        {
            if (_isClosed) return;

            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = themeTag switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }

            if (_appWindow != null && _appWindow.TitleBar != null)
            {
                _appWindow.TitleBar.PreferredTheme = themeTag switch
                {
                    "Light" => Microsoft.UI.Windowing.TitleBarTheme.Light,
                    "Dark" => Microsoft.UI.Windowing.TitleBarTheme.Dark,
                    _ => Microsoft.UI.Windowing.TitleBarTheme.UseDefaultAppMode
                };
            }
        }

        private void UpdateAcrylicProperties()
        {
            if (_isClosed) return;

            if (_acrylicController != null)
            {
                Windows.UI.Color targetColor;
                if (SettingsService.Instance.UseAccentColor)
                {
                    // the live accent color
                    targetColor = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
                }
                else
                {
                    targetColor = SettingsService.Instance.CustomTintColor;
                }

                _acrylicController.TintColor = targetColor;
                _acrylicController.TintOpacity = SettingsService.Instance.TintOpacity;
                _acrylicController.LuminosityOpacity = SettingsService.Instance.LuminosityOpacity;
            }
        }

        private void UpdateSolidBackground()
        {
            if (_isClosed) return;

            // only for the solid material ("None")
            if (SettingsService.Instance.BackdropType == "None")
            {
                // the same color resolution as UpdateAcrylicProperties
                Windows.UI.Color targetColor = SettingsService.Instance.UseAccentColor
                    ? (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]
                    : SettingsService.Instance.CustomTintColor;

                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(targetColor);
            }
        }

        // for a pure OS accent change; both calls resolve the accent fresh, no rebuild needed
        public void RefreshAccentSurfaces()
        {
            UpdateAcrylicProperties();
            UpdateSolidBackground();
        }

        // applies the backdrop material from the settings, per the Microsoft guide:
        // https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops
        public void SetBackdrop(string backdropType)
        {
            if (_isClosed) return;

            DispatcherQueue.EnsureSystemDispatcherQueue();

            if (_configurationSource == null)
            {
                _configurationSource = new SystemBackdropConfiguration();
                this.Activated += Window_Activated;
                ((FrameworkElement)this.Content).ActualThemeChanged += Window_ThemeChanged;

                _configurationSource.IsInputActive = true;
                SetConfigurationSourceTheme();
            }

            // drop the current controller
            _acrylicController?.Dispose();
            _acrylicController = null;
            _micaController?.Dispose();
            _micaController = null;

            if (backdropType == "Acrylic" && DesktopAcrylicController.IsSupported())
            {
                _acrylicController = new DesktopAcrylicController();
                _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
                _acrylicController.SetSystemBackdropConfiguration(_configurationSource);

                UpdateAcrylicProperties();

                // transparent, so the material shows
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else if (backdropType == "Mica" && MicaController.IsSupported())
            {
                _micaController = new MicaController();
                _micaController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
                _micaController.SetSystemBackdropConfiguration(_configurationSource);

                // transparent, so the material shows
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else
            {
                // solid
                UpdateSolidBackground();
            }
        }

        private void Window_ThemeChanged(FrameworkElement sender, object args)
        {
            SetConfigurationSourceTheme();
        }

        private void SetConfigurationSourceTheme()
        {
            if (_configurationSource != null && this.Content is FrameworkElement frameworkElement)
            {
                _configurationSource.Theme = frameworkElement.ActualTheme switch
                {
                    ElementTheme.Dark => SystemBackdropTheme.Dark,
                    ElementTheme.Light => SystemBackdropTheme.Light,
                    _ => SystemBackdropTheme.Default
                };
            }
        }

        // --- workaround: DWM backdrop swapchain kick ---
        // problem: after a Windows transparency or theme change, DesktopAcrylicController needs a rebind to attach its
        // blur to the new DWM swapchain
        // fix: after a rebuild, kick the backdrop once (None, then the current one), with parameters only
        private void KickBackdropRefresh()
        {
            if (_isClosed) return;

            string currentBackdrop = SettingsService.Instance.BackdropType;
            if (currentBackdrop == "Mica" || currentBackdrop == "Acrylic")
            {
                var timer = this.DispatcherQueue.CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(80);
                timer.IsRepeating = false;
                timer.Tick += (s, e) =>
                {
                    if (_isClosed) return;
                    SetBackdrop("None");
                    SetBackdrop(currentBackdrop);
                };
                timer.Start();
            }
        }
    }
}
