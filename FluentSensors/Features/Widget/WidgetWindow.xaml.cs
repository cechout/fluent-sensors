using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using Windows.Foundation;
using WinUIEx;

using FluentSensors.Common.Localization;
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

        // each window has its own sensors, z-order, time range and place; the material is shared
        public const int MaxWidgetWindows = 3;

        private AppWindow _appWindow;
        private readonly string _windowKey;

        // resize floor in DIP; MinPanelHeight per pinned sensor, MinWidgetWidth for the window
        private const int MinPanelHeight = 90;
        private const int MinWidgetWidth = 220;

        // in DIP; title bar 26, graph padding 8 above and below, bottom bar 31 (divider, padding, 22 buttons)
        private const int ChromeHeight = 73;

        public WidgetViewModel ViewModel { get; }
        public int Index { get; } // 0 based, shown as "Widget 1" and up

        // per index: the open window, and the one hidden by its X for reuse
        private static readonly WidgetWindow?[] _openInstances = new WidgetWindow?[MaxWidgetWindows];
        private static readonly WidgetWindow?[] _retainedInstances = new WidgetWindow?[MaxWidgetWindows];
        public static event Action WidgetStateChanged;

        // system backdrop
        private readonly WindowBackdrop _backdrop;
        private Windows.UI.ViewManagement.UISettings? _uiSettings;

        // z-order; the desktop state is the pin
        private WindowZOrder _zOrder;
        private readonly WinDesktopPin _desktopPin;

        // --- z-order icon ---
        // in px on its 15 px grid, at half pixels; round caps add half a px at each end
        private const double ZOrderBarWidth = 15; // centered on the arrow
        private const double ZOrderArrowTip = 2.5; // 1 px clear of a bar at 0.5
        private const double ZOrderArrowEnd = 12.5; // 1 px clear of a bar at 14.5
        private const double ZOrderArrowArm = 3; // the head per side, at 45 degrees; 1 px clear of the middle bar
        // the accent flash of the bar after a click, in ms; held, then faded back
        private const int ZOrderFlashHoldMs = 1000;
        private const int ZOrderFlashFadeMs = 500;
        private Storyboard? _zOrderFlash;


        // === constructor ===

        public WidgetWindow(int index, List<SensorRowViewModel> selectedSensors)
        {
            Index = index;
            _windowKey = GetWindowKey(index);
            var savedState = WindowStateService.Instance.GetState(_windowKey);

            // a window that never picked a time range starts on the setting
            ViewModel = new WidgetViewModel(selectedSensors,
                savedState?.GraphTimeSpanSeconds ?? SettingsService.Instance.GraphTimeSpanSeconds);
            this.InitializeComponent();
            this.AppWindow.SetIcon("Assets\\Icon\\Icon.ico");
            _openInstances[index] = this;
            WidgetStateChanged?.Invoke();

            // a click on empty space hides the keyboard focus rectangle again
            PointerFocusReset.Attach(Content);

            // window configuration
            _appWindow = this.AppWindow;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomTitleBar);

            // always on top follows the z-order, see ApplyZOrder
            var presenter = OverlappedPresenter.Create();
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
            presenter.IsResizable = true;
            _appWindow.SetPresenter(presenter);

            // resize floor for the pinned sensor count; (ReconfigureFor recalculates it)
            ApplyMinimumWindowSize(selectedSensors.Count);

            // restores X, Y and width when they land on a connected monitor, otherwise only sizes the window and
            // Windows places it; the height always follows the current sensor count
            double scaleFactor = GetScaleFactor();
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

            _desktopPin = new WinDesktopPin(WinRT.Interop.WindowNative.GetWindowHandle(this), _appWindow, DispatcherQueue);
            ApplyZOrder(savedState?.ZOrder ?? WindowZOrder.AlwaysOnTop);

            // marked open, so it reopens on the next launch (with the sensors from SensorSelectionService)
            SaveWindowState();

            // theming
            _backdrop = new WindowBackdrop(this, RootGrid, () => SettingsService.Instance.WidgetBackdrop);
            _backdrop.Apply(SettingsService.Instance.BackgroundMaterial);
            ApplyTheme(SettingsService.Instance.AppTheme);

            // event routing
            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            SettingsService.Instance.BackgroundMaterialChanged += OnBackgroundMaterialChanged;
            SettingsService.Instance.OpacityChanged += OnOpacityChanged;
            SettingsService.Instance.TintColorChanged += OnTintColorChanged;

            // the time range of this window
            TimeRangePicker.SelectedSeconds = ViewModel.TimeSpanSeconds;
            TimeRangePicker.RegisterPropertyChangedCallback(TimeRangePickerControl.SelectedSecondsProperty, OnTimeRangePicked);

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

            _backdrop.KickRefresh();
        }


        // === public methods ===

        // the window state key; "Widget" for the first, so it keeps the state it had as the only widget window
        public static string GetWindowKey(int index) => index == 0 ? "Widget" : $"Widget{index + 1}";

        public static WidgetWindow? GetOpenInstance(int index) => _openInstances[index];

        public static IEnumerable<WidgetWindow> OpenInstances => _openInstances.OfType<WidgetWindow>();

        // shows the widget of this index, reusing its hidden window if there is one
        public static void ShowWithSensors(int index, List<SensorRowViewModel> selectedSensors)
        {
            // already open: swap the content and resize in place
            var openWindow = _openInstances[index];
            if (openWindow != null)
            {
                openWindow.ReconfigureFor(selectedSensors);
                openWindow.Activate();
                return;
            }

            // hidden by its X: reuse that window
            var window = _retainedInstances[index];
            if (window != null)
            {
                _retainedInstances[index] = null;

                window.ReconfigureFor(selectedSensors);
                _openInstances[index] = window;
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
            var newWindow = new WidgetWindow(index, selectedSensors);
            newWindow.Activate();
        }

        // brings the widget back without touching its content, for the tray; a no-op unless it is open
        public static void RestoreIfOpen(int index)
        {
            var window = _openInstances[index];
            if (window == null) return;

            if (window._appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Restore();
            }
            window._appWindow.Show();
            window.Activate();
        }

        // every open one, for the tray single click
        public static void RestoreAllOpen()
        {
            for (int i = 0; i < MaxWidgetWindows; i++)
            {
                RestoreIfOpen(i);
            }
        }

        private bool _isClosed = false;

        public void SafeDestroy()
        {
            if (_isClosed) return;
            _isClosed = true;

            try
            {
                SettingsService.Instance.ThemeChanged -= OnThemeChanged;
                SettingsService.Instance.BackgroundMaterialChanged -= OnBackgroundMaterialChanged;
                SettingsService.Instance.OpacityChanged -= OnOpacityChanged;
                SettingsService.Instance.TintColorChanged -= OnTintColorChanged;
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
            // a retained instance; WidgetWindow_Closed stays, it does the real teardown
            try
            {
                _appWindow.Closing -= AppWindow_Closing;
                _appWindow.Changed -= AppWindow_Changed;
            }
            catch { }

            try
            {
                _desktopPin.Release();
                _backdrop.Dispose();
                this.Close();
            }
            catch { }
        }

        private static readonly bool[] _isRecreating = new bool[MaxWidgetWindows];

        // destroys and rebuilds every widget on an OS theme or transparency change
        public static void RecreateWindows()
        {
            for (int i = 0; i < MaxWidgetWindows; i++)
            {
                RecreateWindow(i);
            }
        }

        private static void RecreateWindow(int index)
        {
            if (_isRecreating[index]) return;
            if (_openInstances[index] == null && _retainedInstances[index] == null) return;
            _isRecreating[index] = true;

            var ids = SensorSelectionService.Instance.GetSelection(SensorSelectionProfile.WidgetWindow, index);
            var sensors = ResolveSensors(ids);
            var openWindow = _openInstances[index];
            bool wasVisible = openWindow != null && openWindow._appWindow != null && openWindow._appWindow.IsVisible;

            if (openWindow != null)
            {
                _openInstances[index] = null;
                openWindow.SafeDestroy();
            }

            var retainedWindow = _retainedInstances[index];
            if (retainedWindow != null)
            {
                _retainedInstances[index] = null;
                retainedWindow.SafeDestroy();
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
                        ShowWithSensors(index, sensors);
                    }
                    finally
                    {
                        _isRecreating[index] = false;
                    }
                });

                if (!queued)
                {
                    _isRecreating[index] = false;
                }
            }
            else
            {
                _isRecreating[index] = false;
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
            SettingsService.Instance.BackgroundMaterialChanged -= OnBackgroundMaterialChanged;
            SettingsService.Instance.OpacityChanged -= OnOpacityChanged;
            SettingsService.Instance.TintColorChanged -= OnTintColorChanged;
            SettingsService.Instance.ThemeChanged -= OnThemeChanged;

            // the HardwareMonitorService subscription
            ViewModel.Cleanup();

            // backdrop controllers, per the Microsoft docs
            _desktopPin.Release();
            _backdrop.Dispose();

            // a rebuild may have put its successor in place already
            if (_openInstances[Index] == this)
            {
                _openInstances[Index] = null;
            }
            WidgetStateChanged?.Invoke();
        }

        // --- memory leak: WidgetWindow never released after close ---
        // problem: WinUI 3 never releases a closed secondary Window (confirmed, still open, even with empty content):
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // fix: hide instead of closing and keep the instance in _retainedInstances (same as HiddenSensorsWindow);
        // controllers, settings events and the ViewModel stay alive for the reuse
        // always, whatever MinimizeToTray says; quitting is decided by MainWindow and the tray Exit, never here
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // SafeDestroy is closing for real; a closed window in _retainedInstances would ignore every
            // later SetBackdrop and ApplyTheme
            if (_isClosed) return;

            args.Cancel = true;

            SaveWindowState(wasOpen: false);
            _openInstances[Index] = null;
            _retainedInstances[Index] = this;
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
            ViewModel.SetTimeSpan(TimeRangePicker.SelectedSeconds);
            SaveWindowState();
        }

        // on top, normal, desktop, on top
        private void ZOrderButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyZOrder(_zOrder switch
            {
                WindowZOrder.AlwaysOnTop => WindowZOrder.Normal,
                WindowZOrder.Normal => WindowZOrder.Desktop,
                _ => WindowZOrder.AlwaysOnTop
            });
            SaveWindowState();
            FlashZOrderBar();
        }

        // the accent copy of the bar shows at once and fades out again; a click during the fade starts it over
        private void FlashZOrderBar()
        {
            _zOrderFlash?.Stop();

            var opacity = new DoubleAnimationUsingKeyFrames();
            opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1 });
            opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(ZOrderFlashHoldMs), Value = 1 });
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(ZOrderFlashHoldMs + ZOrderFlashFadeMs),
                Value = 0,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
            Storyboard.SetTarget(opacity, ZOrderBarAccent);
            Storyboard.SetTargetProperty(opacity, "Opacity");

            _zOrderFlash = new Storyboard();
            _zOrderFlash.Children.Add(opacity);
            _zOrderFlash.Begin();
        }

        // icon, tooltip and name follow the state, like the pause button
        private void ApplyZOrder(WindowZOrder zOrder)
        {
            _zOrder = zOrder;

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                if (zOrder == WindowZOrder.Desktop)
                {
                    // no minimize; without a taskbar button a minimized widget only comes back from the tray
                    presenter.IsAlwaysOnTop = false;
                    presenter.IsMinimizable = false;
                    _desktopPin.Pin();
                }
                else
                {
                    _desktopPin.Unpin();
                    presenter.IsMinimizable = true;
                    presenter.IsAlwaysOnTop = zOrder == WindowZOrder.AlwaysOnTop; // readable over other apps
                }
            }

            // only the bar moves: over the arrow, through its middle, under it
            (string labelKey, double barY) = zOrder switch
            {
                WindowZOrder.Normal => ("Widget_ZOrderNormal", 7.5),
                WindowZOrder.Desktop => ("Widget_ZOrderDesktop", 14.5),
                _ => ("Widget_ZOrderAlwaysOnTop", 0.5)
            };
            ZOrderIcon.Data = BuildZOrderGeometry(barY);
            ZOrderBarAccent.X1 = 7.5 - ZOrderBarWidth / 2;
            ZOrderBarAccent.X2 = 7.5 + ZOrderBarWidth / 2;
            ZOrderBarAccent.Y1 = ZOrderBarAccent.Y2 = barY;

            string label = AppStrings.Get(labelKey);
            ToolTipService.SetToolTip(ZOrderButton, label);
            AutomationProperties.SetName(ZOrderButton, label);
        }

        // shaft, head and bar as open figures of one geometry
        private static PathGeometry BuildZOrderGeometry(double barY)
        {
            const double center = 7.5;
            var geometry = new PathGeometry();
            geometry.Figures.Add(BuildOpenFigure(new Point(center, ZOrderArrowTip), new Point(center, ZOrderArrowEnd)));
            geometry.Figures.Add(BuildOpenFigure(
                new Point(center - ZOrderArrowArm, ZOrderArrowTip + ZOrderArrowArm),
                new Point(center, ZOrderArrowTip),
                new Point(center + ZOrderArrowArm, ZOrderArrowTip + ZOrderArrowArm)));
            geometry.Figures.Add(BuildOpenFigure(
                new Point(center - ZOrderBarWidth / 2, barY), new Point(center + ZOrderBarWidth / 2, barY)));
            return geometry;
        }

        private static PathFigure BuildOpenFigure(Point start, params Point[] points)
        {
            var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
            foreach (var point in points)
            {
                figure.Segments.Add(new LineSegment { Point = point });
            }
            return figure;
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
            string label = AppStrings.Get(ViewModel.IsPaused ? "Common_Resume" : "Common_Pause");
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

        private void OnBackgroundMaterialChanged(BackdropMaterial material)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                _backdrop.Apply(material);
            });
        }

        private void OnOpacityChanged(float tintOpacity, float luminosityOpacity)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                _backdrop.Refresh();
            });
        }

        private void OnTintColorChanged(bool useAccentColor, Windows.UI.Color customColor)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                _backdrop.Refresh();
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
            var savedState = WindowStateService.Instance.GetState(_windowKey);
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

        // writes rect, open state, z-order and time range (debounced); the pinned sensors belong to
        // SensorSelectionService
        private void SaveWindowState(bool wasOpen = true)
        {
            var state = WindowStateService.Instance.GetState(_windowKey) ?? new Persistence.Models.WindowState();

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
            state.ZOrder = _zOrder;
            state.GraphTimeSpanSeconds = ViewModel.TimeSpanSeconds;

            WindowStateService.Instance.SetState(_windowKey, state);
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

        // for a pure OS accent change; resolves the accent fresh, no rebuild needed
        public void RefreshAccentSurfaces()
        {
            _backdrop.Refresh();
        }
    }
}
