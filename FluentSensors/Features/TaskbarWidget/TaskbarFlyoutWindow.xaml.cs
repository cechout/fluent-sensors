using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using WinRT;
using WinUIEx;
using WinUIEx.Messaging;
using Microsoft.UI.Xaml.Controls;

using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Controls.SensorRow;
using FluentSensors.Controls.TimeRange;
using FluentSensors.Core.Taskbar;
using FluentSensors.Persistence.Models;
using FluentSensors.Persistence.Services;
using FluentSensors.Features.Widget;
using FluentSensors.Features.CsvLogging;


namespace FluentSensors.Features.TaskbarWidget
{
    // the taskbar flyout:
    // the live graphs of the taskbar profile, anchored next to the taskbar widget; WinUI has no way to anchor a
    // borderless window to a shell window, so this combines:
    // 1. no titlebar stripe, WM_NCCALCSIZE returning 0
    // 2. a height capped against the work area, the graph list scrolls past it
    // 3. Z-order directly beneath Shell_TrayWnd, so it slides out from under the taskbar
    // 4. a real window slide on CompositionTarget.Rendering plus a composition opacity fade
    // 5. DWM corner and shadow settings that follow the Windows transparency setting
    // 6. a DesktopAcrylicController backdrop with a swapchain kick after a rebuild
    //
    // references:
    // https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.input.inputnonclientpointersource
    // https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-nccalcsize
    // https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
    // https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops
    public sealed partial class TaskbarFlyoutWindow : Window
    {
        // === win32 api imports ===

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, uint cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out NativeMethods.RECT pvAttribute, int cbAttribute);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
        private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
        private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private const int GCL_STYLE = -26;
        private const int CS_DROPSHADOW = 0x00020000;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const uint DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

        private const int GWL_STYLE = -16;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_CAPTION = 0x00C00000;

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private const uint DWMWA_NCRENDERING_POLICY = 2;
        private const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        private enum DWMNCRENDERINGPOLICY
        {
            DWMNCRP_USEWINDOWSTYLE = 0,
            DWMNCRP_DISABLED = 1, // no shadow
            DWMNCRP_ENABLED = 2   // standard shadow
        }

        private enum DWM_WINDOW_CORNER_PREFERENCE
        {
            DWMWCP_DEFAULT = 0,
            DWMWCP_DONOTROUND = 1,
            DWMWCP_ROUND = 2, // 8px standard
            DWMWCP_ROUNDSMALL = 3 // 4px
        }


        // --- animation & performance settings ---

        public const int WindowSlideDistanceDip = 300;

        // in ms
        public const int EnterAnimationDurationMs = 240;
        public const int ExitAnimationDurationMs = 140;

        // scaling with the pinned sensor count; durations and distance are tuned for AnimationReferenceSensorCount,
        // each sensor above or below scales them by its factor (0 = fixed, 0.18 = 18 percent per sensor)
        public const int AnimationReferenceSensorCount = 2;
        public const double EnterDurationPerSensorFactor = 0.20;
        public const double ExitDurationPerSensorFactor = 0.14;
        public const double SlideDistancePerSensorFactor = 0.10;

        // clamps the scaling, so one sensor does not snap open and a full flyout does not crawl
        public const double MinAnimationScaleFactor = 0.75;
        public const double MaxAnimationScaleFactor = 3.0;

        // fade opacity
        public const float EnterFadeStartOpacity = 0.0f;
        public const float EnterFadeEndOpacity = 1.0f;
        public const float ExitFadeEndOpacity = 0.9f;

        // keeps the graphs rendering while hidden, so they are current the moment the flyout opens
        public const bool KeepFlyoutGraphsActiveInBackground = true;

        // --- mica preset (BackdropType "Mica" with Windows transparency on) ---
        // over a flat backdrop the controller resolves to lerp(backdrop, tint, luminosity); the native shell flyouts
        // measure as 4 percent backdrop transmission in dark and 9 in light
        // no TintOpacity: the tint blend carries hue and saturation only, so a gray tint makes it a no-op (the source
        // names its BlendEffectMode swapped, the tint layer reads as Luminosity there):
        // https://github.com/microsoft/microsoft-ui-xaml/blob/6aed8d97fdecfe9b19d70c36bd1dacd9c6add7c1/dev/Materials/Acrylic/AcrylicBrush.cpp
        // tints are the Fluent acrylic base tones, AcrylicBackgroundFillColorBaseBrush, not pre-compensated (the
        // render shift applies once to the finished composite):
        // https://github.com/microsoft/microsoft-ui-xaml/blob/6aed8d97fdecfe9b19d70c36bd1dacd9c6add7c1/dev/Materials/Acrylic/AcrylicBrush_19h1_themeresources.xaml
        public static readonly Windows.UI.Color MicaPresetDarkTintColor = Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20);
        public const float MicaPresetDarkLuminosity = 0.96f;

        public static readonly Windows.UI.Color MicaPresetLightTintColor = Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3);
        public const float MicaPresetLightLuminosity = 0.91f;


        // === fields ===

        private const string WindowKey = "TaskbarFlyout";

        // anchor gaps; (on a horizontal taskbar the taskbar gap is also the gap to the far side of the work
        // area, which caps the height)
        public const int FlyoutMarginToTaskbarDip = 12;
        public const int FlyoutMarginToScreenEdgeDip = 12; // along the taskbar

        // inward offset from the widget edge TaskbarFlyoutAlignment picks; (Center ignores it)
        public const int FlyoutAlignmentOffsetDip = 0;

        // fixed width
        public const double FlyoutDefaultWidthDip = AppSettingsData.TaskbarFlyoutWidthDip;

        // height of one graph slot; (the setting)
        private static double FlyoutGraphHeightDip => SettingsService.Instance.TaskbarFlyoutGraphHeightDip;

        // --- flyout layout ---
        // insets and control heights of the rows, from the FlyoutRootBorder resources in the xaml; the height math
        // reads them, since it runs before the rows are ever measured
        private Thickness FlyoutGraphsMargin => LayoutResource<Thickness>("FlyoutGraphsMargin");
        private double FlyoutGraphSpacingDip => LayoutResource<double>("FlyoutGraphSpacing");
        private double FlyoutBarButtonHeightDip => LayoutResource<double>("FlyoutBarButtonHeight");
        private Thickness FlyoutTitleRowPadding => LayoutResource<Thickness>("FlyoutTitleRowPadding");
        private Thickness FlyoutBottomBarPadding => LayoutResource<Thickness>("FlyoutBottomBarPadding");
        private double FlyoutBottomBarSeparatorDip => LayoutResource<Thickness>("FlyoutBottomBarSeparatorThickness").Top;
        private Thickness FlyoutTimeRangeRowPadding => LayoutResource<Thickness>("FlyoutTimeRangeRowPadding");
        private double FlyoutTimeRangePickerHeightDip => LayoutResource<double>("FlyoutTimeRangePickerHeight");

        // the rows in the height math
        private double FlyoutTitleRowHeightDip =>
            FlyoutTitleRowPadding.Top + FlyoutBarButtonHeightDip + FlyoutTitleRowPadding.Bottom;
        private double FlyoutTimeRangeRowHeightDip =>
            FlyoutTimeRangeRowPadding.Top + FlyoutTimeRangePickerHeightDip + FlyoutTimeRangeRowPadding.Bottom;
        private double FlyoutBottomBarHeightDip =>
            FlyoutBottomBarSeparatorDip + FlyoutBottomBarPadding.Top + FlyoutBarButtonHeightDip + FlyoutBottomBarPadding.Bottom;

        private AppWindow _appWindow;
        private IntPtr _hwnd;
        private WindowMessageMonitor _messageMonitor;
        private UISettings? _uiSettings;

        private bool _isAnchored; // set once PositionNextToTaskbar has placed the window
        private int _targetX;
        private int _targetY;
        private bool _isAdjustingPosition;
        private bool _isHiding;

        // sensor count the slide is scaled by; (capped at what fits under the height cap)
        private int _animationSensorCount = AnimationReferenceSensorCount;

        // the items panel, cached on the first layout pass (an ItemsPanelTemplate cannot be named);
        // _isGraphsScrolling holds the mode until then
        private FluentSensors.Controls.VerticalStretchPanel? _graphsPanel;
        private bool _isGraphsScrolling;

        // native window slide, ticked from CompositionTarget.Rendering; (a DispatcherTimer caps out near 60 fps, the
        // compositor tick follows the display refresh rate)
        private bool _isAnimating;
        private Stopwatch? _animStopwatch;
        private int _animStartX, _animStartY, _animTargetX, _animTargetY;
        private int _animDurationMs;
        private bool _animIsEntering;
        private Action? _animOnComplete;

        // slide offset in px, from the target toward the taskbar; set per open from the taskbar DPI (recomputing it
        // against the window DPI could jump the first frame on a mixed-DPI setup)
        private int _slideOffsetX;
        private int _slideOffsetY;

        public TaskbarWidgetViewModel ViewModel { get; }
        public static TaskbarFlyoutWindow? CurrentInstance { get; private set; }
        private static TaskbarFlyoutWindow? _retainedInstance;

        // system backdrop; (no MicaController, "Mica" runs through _acrylicController too, see SetBackdrop)
        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _configurationSource;


        // === constructor ===

        public TaskbarFlyoutWindow(TaskbarWidgetViewModel viewModel)
        {
            ViewModel = viewModel;
            this.InitializeComponent();
            CurrentInstance = this;

            // a click on empty space hides the keyboard focus rectangle again
            PointerFocusReset.Attach(Content);

            _appWindow = this.AppWindow;
            _appWindow.IsShownInSwitchers = false;
            _appWindow.SetIcon("Assets\\Icon\\Icon.ico");
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            // frameless presenter, no caption buttons
            var presenter = OverlappedPresenter.Create();
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = false; // sits behind the taskbar
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false; // height follows the sensor count
            _appWindow.SetPresenter(presenter);

            // popup tool window without frame or caption
            int style = GetWindowLong(_hwnd, GWL_STYLE);
            SetWindowLong(_hwnd, GWL_STYLE, (style | WS_POPUP) & ~WS_THICKFRAME & ~WS_CAPTION);

            int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

            // strip CS_DROPSHADOW class style
            try
            {
                IntPtr classStyle = GetClassLongPtr(_hwnd, GCL_STYLE);
                long newClassStyle = classStyle.ToInt64() & ~CS_DROPSHADOW;
                SetClassLongPtr(_hwnd, GCL_STYLE, new IntPtr(newClassStyle));
            }
            catch { }

            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            // win32 hooks; WM_NCCALCSIZE drops the 8px titlebar stripe, WM_SYSCOMMAND blocks moving
            _messageMonitor = new WindowMessageMonitor(_hwnd);
            _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;

            // shadow policy follows the Windows transparency setting
            InitializeShadowPolicy();

            // theming and backdrop, from the taskbar settings
            SetBackdrop(SettingsService.Instance.TaskbarBackdropType);
            ApplyTheme(SettingsService.Instance.AppTheme);

            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            SettingsService.Instance.TaskbarBackdropTypeChanged += OnBackdropTypeChanged;
            SettingsService.Instance.TaskbarOpacityChanged += OnOpacityChanged;
            SettingsService.Instance.TaskbarTintColorChanged += OnTintColorChanged;
            SettingsService.Instance.TaskbarFlyoutAlignmentChanged += OnFlyoutAlignmentChanged;
            SettingsService.Instance.TaskbarFlyoutGraphHeightChanged += OnFlyoutGraphHeightChanged;

            // the two time ranges; the pickers and the settings page write the same settings
            TaskbarTimeRangePicker.SelectedSeconds = SettingsService.Instance.TaskbarGraphTimeSpanSeconds;
            FlyoutTimeRangePicker.SelectedSeconds = SettingsService.Instance.TaskbarFlyoutGraphTimeSpanSeconds;
            TaskbarTimeRangePicker.RegisterPropertyChangedCallback(TimeRangePickerControl.SelectedSecondsProperty, OnTaskbarTimeRangePicked);
            FlyoutTimeRangePicker.RegisterPropertyChangedCallback(TimeRangePickerControl.SelectedSecondsProperty, OnFlyoutTimeRangePicked);
            SettingsService.Instance.TaskbarGraphTimeSpanChanged += OnTaskbarGraphTimeSpanChanged;
            SettingsService.Instance.TaskbarFlyoutGraphTimeSpanChanged += OnFlyoutGraphTimeSpanChanged;

            // a rebuilt window takes over a running snapshot with the view model
            ApplyPauseState();

            FlyoutShortcutRegistration.RegistrationChanged += OnShortcutRegistrationChanged;
            ApplyShortcutHint();

            ((FrameworkElement)this.Content).ActualThemeChanged += (s, e) =>
            {
                if (_isClosed) return;
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    if (_isClosed) return;
                    SetConfigurationSourceTheme();
                    UpdateAcrylicProperties();
                    UpdateSolidBackground();
                });
            };

            GraphsItemsControl.LayoutUpdated += OnGraphsItemsControlLayoutUpdated;

            _appWindow.Changed += AppWindow_Changed;
            FlyoutRootBorder.SizeChanged += FlyoutRootBorder_SizeChanged;
            _appWindow.Closing += AppWindow_Closing;
            this.Activated += Window_Activated;

            KickBackdropRefresh();
        }


        // === shadow policy ===

        private void InitializeShadowPolicy()
        {
            try
            {
                _uiSettings = new UISettings();
                SeedSystemVisualsSnapshot(_uiSettings);
                _uiSettings.AdvancedEffectsEnabledChanged += OnSystemVisualSettingsChanged;
                _uiSettings.ColorValuesChanged += OnSystemVisualSettingsChanged;
                UpdateShadowPolicy();
            }
            catch
            {
                // UISettings unavailable; no shadow policy
            }
        }

        // named so SafeDestroy can detach it (every rebuilt window subscribes anew); goes through the router, since
        // ColorValuesChanged also fires for a pure accent change that needs no rebuild
        private void OnSystemVisualSettingsChanged(UISettings sender, object args)
        {
            RouteSystemVisualsChange(sender);
        }

        private void UpdateShadowPolicy()
        {
            if (_hwnd == IntPtr.Zero) return;

            bool isTransparencyEnabled = _uiSettings != null && _uiSettings.AdvancedEffectsEnabled;

            if (isTransparencyEnabled)
            {
                // transparency on; DWM rounds the corners and clips the acrylic (the native shadow comes with it)
                int cornerPreference = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
                DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

                var margins = new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 1, cyBottomHeight = 0 };
                DwmExtendFrameIntoClientArea(_hwnd, ref margins);

                int policy = (int)DWMNCRENDERINGPOLICY.DWMNCRP_DISABLED;
                DwmSetWindowAttribute(_hwnd, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));
            }
            else
            {
                // transparency off; no DWM rounding and no shadow, the XAML border draws the 8px corners
                int cornerPreference = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

                var margins = new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 0, cyBottomHeight = 0 };
                DwmExtendFrameIntoClientArea(_hwnd, ref margins);

                int policy = (int)DWMNCRENDERINGPOLICY.DWMNCRP_DISABLED;
                DwmSetWindowAttribute(_hwnd, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));

                int borderColor = DWMWA_COLOR_NONE;
                DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            }

            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }


        // === win32 message handling ===

        private void OnWindowMessageReceived(object? sender, WindowMessageEventArgs e)
        {
            if (e.Message.MessageId == 0x0083) // WM_NCCALCSIZE
            {
                if (e.Message.WParam != 0)
                {
                    // client area covers the whole window rect
                    e.Result = IntPtr.Zero;
                    e.Handled = true;
                }
            }
            else if (e.Message.MessageId == 0x0112) // WM_SYSCOMMAND
            {
                if ((e.Message.WParam.ToUInt32() & 0xFFF0) == 0xF010) // SC_MOVE
                {
                    e.Result = IntPtr.Zero;
                    e.Handled = true;
                }
            }
        }


        // === public methods ===

        // re-places the flyout for the current pinned sensor count
        public static void ResetGeometry()
        {
            if (CurrentInstance != null && TaskbarWidgetWindow.CurrentInstance != null)
            {
                CurrentInstance.PositionNextToTaskbar(TaskbarWidgetWindow.CurrentInstance, startForSlideAnimation: false);
            }
            else if (_retainedInstance != null && TaskbarWidgetWindow.CurrentInstance != null)
            {
                _retainedInstance.PositionNextToTaskbar(TaskbarWidgetWindow.CurrentInstance, startForSlideAnimation: false);
            }
        }

        // builds the flyout at taskbar startup, so the first open has no latency
        public static void Preload(TaskbarWidgetWindow widgetWindow)
        {
            // assumes a retained instance is live (a destroyed one here leaves the flyout dead, see AppWindow_Closing)
            if (widgetWindow == null || CurrentInstance != null || _retainedInstance != null) return;

            var window = new TaskbarFlyoutWindow(widgetWindow.ViewModel);
            _retainedInstance = window;
            window.PositionNextToTaskbar(widgetWindow, startForSlideAnimation: false);

            if (KeepFlyoutGraphsActiveInBackground)
            {
                window.SetGraphsRenderingActive(true);
            }
        }

        private void EnsureBehindTaskbarZOrder()
        {
            var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd != IntPtr.Zero)
            {
                // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOOWNERZORDER
                SetWindowPos(_hwnd, taskbarHwnd, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
            }
        }

        public static void Toggle(TaskbarWidgetWindow widgetWindow)
        {
            if (widgetWindow == null) return;

            if (CurrentInstance != null && CurrentInstance._appWindow.IsVisible && !CurrentInstance._isHiding)
            {
                CurrentInstance.HideFlyout();
                return;
            }

            ShowFlyout(widgetWindow);
        }

        public static void ShowFlyout(TaskbarWidgetWindow widgetWindow)
        {
            if (widgetWindow == null) return;

            if (CurrentInstance != null)
            {
                CurrentInstance.ApplyTheme(SettingsService.Instance.AppTheme);
                CurrentInstance.PositionNextToTaskbar(widgetWindow, startForSlideAnimation: true);
                CurrentInstance.SetGraphsRenderingActive(true);
                CurrentInstance._appWindow.Show();
                CurrentInstance.EnsureBehindTaskbarZOrder();
                CurrentInstance.Activate();
                CurrentInstance.SlideIn();
                return;
            }

            if (_retainedInstance != null)
            {
                var window = _retainedInstance;
                _retainedInstance = null;
                CurrentInstance = window;

                window.ApplyTheme(SettingsService.Instance.AppTheme);
                window.PositionNextToTaskbar(widgetWindow, startForSlideAnimation: true);
                window.SetGraphsRenderingActive(true);
                window._appWindow.Show();
                window.EnsureBehindTaskbarZOrder();
                window.Activate();
                window.SlideIn();
                return;
            }

            var newWindow = new TaskbarFlyoutWindow(widgetWindow.ViewModel);
            newWindow.ApplyTheme(SettingsService.Instance.AppTheme);
            newWindow.PositionNextToTaskbar(widgetWindow, startForSlideAnimation: true);
            newWindow.SetGraphsRenderingActive(true);
            newWindow._appWindow.Show();
            newWindow.EnsureBehindTaskbarZOrder();
            newWindow.Activate();
            newWindow.SlideIn();
        }

        public void HideFlyout()
        {
            if (_isHiding || !_appWindow.IsVisible) return;
            _isHiding = true;

            SlideOut(() =>
            {
                _isHiding = false;
                SetGraphsRenderingActive(false);
                SaveWindowState();
                _appWindow.Hide();
                TaskbarWidgetWindow.CurrentInstance?.SetFlyoutActive(false);
            });
        }

        private bool _isClosed = false;

        // --- memory leak: flyout instance never released after a real close ---
        // problem: WinUI 3 never releases a closed secondary Window (confirmed, still open, even with empty content):
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // fix: none here; the one place that destroys for real, since DWM only rebinds the acrylic on a full
        // recreation (see ScheduleRecreation); one leaked CCW per OS theme or transparency change, knowingly paid
        // only ever called from ExecuteFullRebuild
        public void SafeDestroy()
        {
            if (_isClosed) return;
            _isClosed = true;

            // a slide still in flight would keep the static Rendering event ticking a dead window
            StopSlide();

            try
            {
                SettingsService.Instance.ThemeChanged -= OnThemeChanged;
                SettingsService.Instance.TaskbarBackdropTypeChanged -= OnBackdropTypeChanged;
                SettingsService.Instance.TaskbarOpacityChanged -= OnOpacityChanged;
                SettingsService.Instance.TaskbarTintColorChanged -= OnTintColorChanged;
                SettingsService.Instance.TaskbarFlyoutAlignmentChanged -= OnFlyoutAlignmentChanged;
                SettingsService.Instance.TaskbarFlyoutGraphHeightChanged -= OnFlyoutGraphHeightChanged;
                SettingsService.Instance.TaskbarGraphTimeSpanChanged -= OnTaskbarGraphTimeSpanChanged;
                SettingsService.Instance.TaskbarFlyoutGraphTimeSpanChanged -= OnFlyoutGraphTimeSpanChanged;
                FlyoutShortcutRegistration.RegistrationChanged -= OnShortcutRegistrationChanged;
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

            // detached first, or AppWindow_Closing cancels the Close below and brings this
            // window back as _retainedInstance
            try
            {
                _appWindow.Closing -= AppWindow_Closing;
                _appWindow.Changed -= AppWindow_Changed;
                this.Activated -= Window_Activated;
            }
            catch { }

            try
            {
                _messageMonitor?.Dispose();
                _messageMonitor = null;
                _acrylicController?.Dispose();
                _acrylicController = null;
                _noiseBitmap = null;
                this.Close();
            }
            catch { }
        }

        private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _recreateDebounceTimer;
        private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _accentRefreshDebounceTimer;

        // last seen OS visual state; (ColorValuesChanged does not say whether accent or theme
        // changed, so snapshots are compared)
        private static Windows.UI.Color _lastAccentColor;
        private static Windows.UI.Color _lastBackgroundColor;
        private static bool _lastAdvancedEffectsEnabled;
        private static bool _hasVisualSnapshot;

        // baseline before the first UISettings event, so that event diffs against real state instead of forcing a
        // rebuild (called from both WidgetWindow and this constructor, calling twice is harmless)
        public static void SeedSystemVisualsSnapshot(UISettings settings)
        {
            _lastAccentColor = settings.GetColorValue(UIColorType.Accent);
            _lastBackgroundColor = settings.GetColorValue(UIColorType.Background);
            _lastAdvancedEffectsEnabled = settings.AdvancedEffectsEnabled;
            _hasVisualSnapshot = true;
        }

        // routes a Windows visual settings change to the in-place accent refresh or the full rebuild
        // (both windows land here for the same OS event; the second call sees no difference and does nothing)
        public static void RouteSystemVisualsChange(UISettings sender)
        {
            var accent = sender.GetColorValue(UIColorType.Accent);
            var background = sender.GetColorValue(UIColorType.Background);
            bool advancedEffects = sender.AdvancedEffectsEnabled;

            if (!_hasVisualSnapshot)
            {
                SeedSystemVisualsSnapshot(sender);
                ScheduleRecreation();
                return;
            }

            bool accentChanged = !accent.Equals(_lastAccentColor);
            bool structuralChanged = !background.Equals(_lastBackgroundColor) || advancedEffects != _lastAdvancedEffectsEnabled;

            _lastAccentColor = accent;
            _lastBackgroundColor = background;
            _lastAdvancedEffectsEnabled = advancedEffects;

            if (structuralChanged)
            {
                ScheduleRecreation();
            }
            else if (accentChanged)
            {
                ScheduleAccentRefresh();
            }
        }

        // debounced pure accent change, refreshed in place; (150ms like ScheduleRecreation, a picker drag raises
        // ColorValuesChanged repeatedly)
        private static void ScheduleAccentRefresh()
        {
            var queue = TaskbarWidgetWindow.CurrentInstance?.DispatcherQueue
                ?? MainWindow.CurrentInstance?.DispatcherQueue
                ?? DispatcherQueue.GetForCurrentThread();

            if (queue == null) return;

            queue.TryEnqueue(() =>
            {
                if (_accentRefreshDebounceTimer != null)
                {
                    _accentRefreshDebounceTimer.Stop();
                    _accentRefreshDebounceTimer = null;
                }

                _accentRefreshDebounceTimer = queue.CreateTimer();
                _accentRefreshDebounceTimer.Interval = TimeSpan.FromMilliseconds(150);
                _accentRefreshDebounceTimer.IsRepeating = false;
                _accentRefreshDebounceTimer.Tick += (s, e) =>
                {
                    _accentRefreshDebounceTimer?.Stop();
                    _accentRefreshDebounceTimer = null;
                    ExecuteAccentRefresh();
                };
                _accentRefreshDebounceTimer.Start();
            });
        }

        // refreshes the accent surfaces of all three windows: graph colors (and the taskbar card tint riding on them),
        // acrylic tint and solid background; the flyout shares the taskbar widget ViewModel, so its graphs come along
        private static void ExecuteAccentRefresh()
        {
            TaskbarWidgetWindow.CurrentInstance?.ViewModel.RefreshGraphColors();

            var widget = WidgetWindow.CurrentInstance;
            widget?.ViewModel.RefreshGraphColors();
            widget?.RefreshAccentSurfaces();

            var flyout = CurrentInstance ?? _retainedInstance;
            flyout?.RefreshAccentSurfaces();
        }

        // --- workaround: window recreation on global OS theme/transparency change ---
        // problem: after a Windows theme or transparency change, DWM does not bind the DesktopAcrylicController blur
        // without a full window recreation (empirical, cause unknown)
        // fix: destroy and rebuild every open window (this flyout, WidgetWindow, TaskbarWidgetWindow, CsvLoggerWindow);
        // the acrylic ones kick their backdrop from the constructor via KickBackdropRefresh
        // only the Windows-level UISettings events land here; never toggle a persisted setting to force a repaint
        // instead (an interrupted toggle persists its intermediate value)
        public static void ScheduleRecreation()
        {
            var queue = TaskbarWidgetWindow.CurrentInstance?.DispatcherQueue
                ?? MainWindow.CurrentInstance?.DispatcherQueue
                ?? DispatcherQueue.GetForCurrentThread();

            if (queue == null) return;

            queue.TryEnqueue(() =>
            {
                if (_recreateDebounceTimer != null)
                {
                    _recreateDebounceTimer.Stop();
                    _recreateDebounceTimer = null;
                }

                _recreateDebounceTimer = queue.CreateTimer();
                _recreateDebounceTimer.Interval = TimeSpan.FromMilliseconds(150);
                _recreateDebounceTimer.IsRepeating = false;
                _recreateDebounceTimer.Tick += (s, e) =>
                {
                    _recreateDebounceTimer?.Stop();
                    _recreateDebounceTimer = null;
                    ExecuteFullRebuild();
                };
                _recreateDebounceTimer.Start();
            });
        }

        // the destructive half of the ScheduleRecreation workaround and the only caller of SafeDestroy; the flyout
        // goes first and comes back last, since it anchors to the new taskbar widget
        private static void ExecuteFullRebuild()
        {
            bool flyoutWasVisible = CurrentInstance != null && CurrentInstance._appWindow != null && CurrentInstance._appWindow.IsVisible;

            // 1. both flyout instances
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

            // 2. WidgetWindow, if open
            WidgetWindow.RecreateWindow();

            // 3. TaskbarWidgetWindow; (the rebuilt widget reopens the flyout itself once embedded)
            TaskbarWidgetWindow.RecreateWindow(restoreFlyout: flyoutWasVisible);

            // 4. CsvLoggerWindow; (a running recording lives in CsvLoggingService and is unaffected)
            CsvLoggerWindow.RecreateWindow();
        }


        // === window slide and content fade ===

        private void SlideIn()
        {
            TaskbarWidgetWindow.CurrentInstance?.SetFlyoutActive(true);

            int durationMs = ScaleAnimationDuration(EnterAnimationDurationMs, EnterDurationPerSensorFactor);

            // 1. content fade
            PlayContentFade(EnterFadeStartOpacity, EnterFadeEndOpacity, durationMs, isEntering: true);

            // 2. window slide, out from behind the taskbar
            int startX = _targetX + _slideOffsetX;
            int startY = _targetY + _slideOffsetY;

            EnsureBehindTaskbarZOrder();
            AnimateNativeWindowPosition(startX, startY, _targetX, _targetY, durationMs, isEntering: true);
        }

        private void SlideOut(Action onCompleted)
        {
            TaskbarWidgetWindow.CurrentInstance?.SetFlyoutActive(false);

            int durationMs = ScaleAnimationDuration(ExitAnimationDurationMs, ExitDurationPerSensorFactor);

            // 1. content fade
            PlayContentFade(1.0f, ExitFadeEndOpacity, durationMs, isEntering: false);

            // 2. window slide, back behind the taskbar; (only the slide axis moves)
            int currentX = _appWindow.Position.X;
            int currentY = _appWindow.Position.Y;
            int endX = _slideOffsetX != 0 ? _targetX + _slideOffsetX : currentX;
            int endY = _slideOffsetY != 0 ? _targetY + _slideOffsetY : currentY;

            EnsureBehindTaskbarZOrder();
            AnimateNativeWindowPosition(currentX, currentY, endX, endY, durationMs, isEntering: false, onComplete: onCompleted);
        }

        // shared scaling law: how far a value moves from its base with the number of graph rows shown
        private double AnimationScaleFactor(double perSensorFactor)
        {
            double factor = 1.0 + ((_animationSensorCount - AnimationReferenceSensorCount) * perSensorFactor);

            return Math.Clamp(factor, MinAnimationScaleFactor, MaxAnimationScaleFactor);
        }

        private int ScaleAnimationDuration(int baseDurationMs, double perSensorFactor)
        {
            return (int)Math.Round(baseDurationMs * AnimationScaleFactor(perSensorFactor));
        }

        // slide distance in px; (PositionNextToTaskbar parks the window on the same value, so both read it here)
        private int GetSlideDistancePx(double scaleFactor)
        {
            return (int)Math.Round(WindowSlideDistanceDip * AnimationScaleFactor(SlideDistancePerSensorFactor) * scaleFactor);
        }

        // same curve as the window slide, so fade and slide never diverge: ease-out 1-(1-p)^3 on enter, ease-in p^3
        // on exit, as the bezier points (1/3,1)/(2/3,1) and (1/3,0)/(2/3,0) that reduce to exactly those
        private void PlayContentFade(float fromOpacity, float toOpacity, int durationMs, bool isEntering)
        {
            if (RootGrid == null) return;
            var visual = ElementCompositionPreview.GetElementVisual(RootGrid);
            var compositor = visual?.Compositor;
            if (compositor == null) return;

            var easing = isEntering
                ? compositor.CreateCubicBezierEasingFunction(new Vector2(1f / 3f, 1f), new Vector2(2f / 3f, 1f))
                : compositor.CreateCubicBezierEasingFunction(new Vector2(1f / 3f, 0f), new Vector2(2f / 3f, 0f));

            // opacity only; the window itself does the moving
            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.InsertKeyFrame(0.0f, fromOpacity);
            opacityAnim.InsertKeyFrame(1.0f, toOpacity, easing);
            opacityAnim.Duration = TimeSpan.FromMilliseconds(durationMs);
            visual.StartAnimation("Opacity", opacityAnim);
        }

        private void AnimateNativeWindowPosition(int startX, int startY, int targetX, int targetY, int durationMs, bool isEntering, Action? onComplete = null)
        {
            StopSlide();

            _animStartX = startX;
            _animStartY = startY;
            _animTargetX = targetX;
            _animTargetY = targetY;
            _animDurationMs = durationMs;
            _animIsEntering = isEntering;
            _animOnComplete = onComplete;

            _isAdjustingPosition = true;
            SetWindowPos(_hwnd, IntPtr.Zero, startX, startY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            _isAdjustingPosition = false;

            _isAnimating = true;
            _animStopwatch = Stopwatch.StartNew();
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnSlideFrame;
        }

        // ticks with the compositor frame, so the slide keeps pace with the display refresh rate
        private void OnSlideFrame(object? sender, object e)
        {
            if (_animStopwatch == null) return;

            double progress = Math.Clamp((double)_animStopwatch.ElapsedMilliseconds / _animDurationMs, 0.0, 1.0);

            // Fluent 2 easing; enter 1-(1-p)^3, exit p^3
            double ease = _animIsEntering
                ? (1.0 - Math.Pow(1.0 - progress, 3))
                : Math.Pow(progress, 3);

            int currentX = (int)Math.Round(_animStartX + ((_animTargetX - _animStartX) * ease));
            int currentY = (int)Math.Round(_animStartY + ((_animTargetY - _animStartY) * ease));

            _isAdjustingPosition = true;
            SetWindowPos(_hwnd, IntPtr.Zero, currentX, currentY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            _isAdjustingPosition = false;

            if (progress >= 1.0)
            {
                var onComplete = _animOnComplete;
                _animOnComplete = null;
                StopSlide();
                onComplete?.Invoke();
            }
        }

        // every path that ends a slide (completion, a new slide, SafeDestroy) calls this; the static Rendering event
        // otherwise keeps ticking for the life of the process
        private void StopSlide()
        {
            if (!_isAnimating) return;
            _isAnimating = false;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnSlideFrame;
            _animStopwatch?.Stop();
            _animStopwatch = null;
        }


        // === window sizing and positioning ===

        // places the flyout beside the taskbar widget, on the desktop side of the taskbar edge: along the taskbar per
        // TaskbarFlyoutAlignment, FlyoutMarginToTaskbarDip off it, clamped to the primary work area
        private void PositionNextToTaskbar(TaskbarWidgetWindow widgetWindow, bool startForSlideAnimation = false)
        {
            var widgetHwnd = WinRT.Interop.WindowNative.GetWindowHandle(widgetWindow);
            NativeMethods.GetWindowRect(widgetHwnd, out var widgetRect);

            var primaryTaskbar = WinTaskbarService.Instance.DiscoverNow().FirstOrDefault();
            double scale = primaryTaskbar != null ? (primaryTaskbar.Dpi / 96.0) : GetScaleFactor();

            // without a taskbar the widget rect stands in for it, on the default bottom edge
            var edge = primaryTaskbar?.Edge ?? ScreenEdge.Bottom;
            var bar = primaryTaskbar?.Rect
                ?? new RectInt32(widgetRect.Left, widgetRect.Top, widgetRect.Right - widgetRect.Left, widgetRect.Bottom - widgetRect.Top);

            int desiredWidthPx = (int)Math.Round(FlyoutDefaultWidthDip * scale);

            int sensorCount = ViewModel.FlyoutSensors.Count;

            // gaps and alignment offset
            int marginPx = (int)Math.Round(FlyoutMarginToTaskbarDip * scale);
            int offsetPx = (int)Math.Round(FlyoutAlignmentOffsetDip * scale);
            int edgeMarginPx = (int)Math.Round(FlyoutMarginToScreenEdgeDip * scale);

            // height cap: the work area less the taskbar gap at both ends (horizontal taskbar) or the screen edge
            // gap at both ends (vertical), floored at one graph slot; past it the graphs scroll
            var workArea = DisplayArea.Primary.WorkArea;
            int availableHeightPx = edge switch
            {
                ScreenEdge.Top => (workArea.Y + workArea.Height - marginPx) - (bar.Y + bar.Height + marginPx),
                ScreenEdge.Left or ScreenEdge.Right => workArea.Height - (2 * edgeMarginPx),
                _ => (bar.Y - marginPx) - (workArea.Y + marginPx)
            };
            int maxHeightPx = Math.Max(CalculateFlyoutDefaultHeight(1, scale), availableHeightPx);

            int desiredHeightPx = CalculateFlyoutDefaultHeight(sensorCount, scale);
            bool isScrolling = desiredHeightPx > maxHeightPx;
            if (isScrolling)
            {
                desiredHeightPx = maxHeightPx;
            }

            // a capped window stops growing, so the slide scaling stops too
            _animationSensorCount = isScrolling ? CountFittingGraphSlots(maxHeightPx, scale) : sensorCount;
            ApplyGraphsScrollMode(isScrolling);

            if (edge is ScreenEdge.Left or ScreenEdge.Right)
            {
                // beside the taskbar; Left/Right of the alignment setting anchor to the top and bottom widget edge
                _targetX = edge == ScreenEdge.Left
                    ? bar.X + bar.Width + marginPx
                    : bar.X - marginPx - desiredWidthPx;
                _targetY = SettingsService.Instance.TaskbarFlyoutAlignment switch
                {
                    "Left" => widgetRect.Top + offsetPx,
                    "Right" => widgetRect.Bottom - desiredHeightPx - offsetPx,
                    _ => ((widgetRect.Top + widgetRect.Bottom) / 2) - (desiredHeightPx / 2)
                };

                // clamp to the work area with edgeMarginPx; (the top edge wins when both do not fit)
                int bottomLimitY = workArea.Y + workArea.Height - desiredHeightPx - edgeMarginPx;
                int topLimitY = workArea.Y + edgeMarginPx;
                _targetY = Math.Max(topLimitY, Math.Min(_targetY, bottomLimitY));
            }
            else
            {
                // Left/Right anchor to the matching widget edge plus the offset; Center lines up the centers
                _targetX = SettingsService.Instance.TaskbarFlyoutAlignment switch
                {
                    "Left" => widgetRect.Left + offsetPx,
                    "Right" => widgetRect.Right - desiredWidthPx - offsetPx,
                    _ => ((widgetRect.Left + widgetRect.Right) / 2) - (desiredWidthPx / 2)
                };
                _targetY = edge == ScreenEdge.Top
                    ? bar.Y + bar.Height + marginPx
                    : bar.Y - marginPx - desiredHeightPx;

                // clamp to the work area with edgeMarginPx; (the left edge wins when both do not fit, the height cap
                // already keeps the far edge on marginPx)
                int rightLimitX = workArea.X + workArea.Width - desiredWidthPx - edgeMarginPx;
                int leftLimitX = workArea.X + edgeMarginPx;
                _targetX = Math.Max(leftLimitX, Math.Min(_targetX, rightLimitX));
            }

            _isAnchored = true;

            // from the taskbar DPI like the rest of the geometry, see _slideOffsetX
            (_slideOffsetX, _slideOffsetY) = CalculateSlideOffset(edge, GetSlideDistancePx(scale), desiredWidthPx, desiredHeightPx);

            int initialX = startForSlideAnimation ? (_targetX + _slideOffsetX) : _targetX;
            int initialY = startForSlideAnimation ? (_targetY + _slideOffsetY) : _targetY;

            _isAdjustingPosition = true;
            _appWindow.MoveAndResize(new RectInt32(initialX, initialY, desiredWidthPx, desiredHeightPx));
            _isAdjustingPosition = false;

            UpdateShadowPolicy();
        }

        // slide vector from the target toward the taskbar edge, where the window hides behind the taskbar or off
        // screen; cut to the taskbar monitor only when the path would cross a neighboring monitor
        private (int X, int Y) CalculateSlideOffset(ScreenEdge edge, int distancePx, int widthPx, int heightPx)
        {
            var monitor = DisplayArea.Primary.OuterBounds;

            (int X, int Y) direction = edge switch
            {
                ScreenEdge.Top => (0, -1),
                ScreenEdge.Left => (-1, 0),
                ScreenEdge.Right => (1, 0),
                _ => (0, 1)
            };

            // distance until the far window edge reaches the monitor edge
            int roomPx = edge switch
            {
                ScreenEdge.Top => _targetY - monitor.Y,
                ScreenEdge.Left => _targetX - monitor.X,
                ScreenEdge.Right => (monitor.X + monitor.Width) - (_targetX + widthPx),
                _ => (monitor.Y + monitor.Height) - (_targetY + heightPx)
            };

            if (distancePx > roomPx)
            {
                // the whole path from the parked start to the target
                var path = new RectInt32(
                    Math.Min(_targetX, _targetX + (direction.X * distancePx)),
                    Math.Min(_targetY, _targetY + (direction.Y * distancePx)),
                    widthPx + Math.Abs(direction.X * distancePx),
                    heightPx + Math.Abs(direction.Y * distancePx));

                if (OverlapsOtherDisplay(path, monitor))
                {
                    distancePx = Math.Max(0, roomPx);
                }
            }

            return (direction.X * distancePx, direction.Y * distancePx);
        }

        private static bool OverlapsOtherDisplay(RectInt32 rect, RectInt32 ownMonitor)
        {
            // indexed loop; foreach over DisplayArea.FindAll() throws an InvalidCastException (WinRT enumerator bug)
            var displayAreas = DisplayArea.FindAll();
            for (int i = 0; i < displayAreas.Count; i++)
            {
                var bounds = displayAreas[i].OuterBounds;
                if (bounds.Equals(ownMonitor)) continue;

                if (rect.X < bounds.X + bounds.Width && rect.X + rect.Width > bounds.X &&
                    rect.Y < bounds.Y + bounds.Height && rect.Y + rect.Height > bounds.Y)
                {
                    return true;
                }
            }
            return false;
        }

        private double GetScaleFactor()
        {
            uint dpi = GetDpiForWindow(_hwnd);
            return dpi / 96.0;
        }

        private int CalculateFlyoutDefaultHeight(int sensorCount, double scaleFactor)
        {
            return (int)(CalculateFlyoutContentHeight(sensorCount, FlyoutGraphHeightDip) * scaleFactor);
        }

        // graph slots that fit under a capped window height
        private int CountFittingGraphSlots(int maxHeightPx, double scaleFactor)
        {
            double interiorDip = (maxHeightPx / scaleFactor) - FlyoutRowsHeightDip
                - FlyoutGraphsMargin.Top - FlyoutGraphsMargin.Bottom;

            int slots = (int)Math.Floor(
                (interiorDip + FlyoutGraphSpacingDip) / (FlyoutGraphHeightDip + FlyoutGraphSpacingDip));

            return Math.Max(1, slots);
        }

        // the title row, the time range row and the bar strip, everything but the graphs
        private double FlyoutRowsHeightDip =>
            FlyoutTitleRowHeightDip + FlyoutTimeRangeRowHeightDip + FlyoutBottomBarHeightDip;

        // window height for n graph slots: the rows around them, the graphs margin, n slots and the n-1 gaps
        private double CalculateFlyoutContentHeight(int sensorCount, double graphHeightDip)
        {
            if (sensorCount <= 0) return FlyoutRowsHeightDip;

            return FlyoutRowsHeightDip
                + FlyoutGraphsMargin.Top + FlyoutGraphsMargin.Bottom
                + (sensorCount * graphHeightDip)
                + ((sensorCount - 1) * FlyoutGraphSpacingDip);
        }

        // a layout value from the FlyoutRootBorder resources
        private T LayoutResource<T>(string key) => (T)FlyoutRootBorder.Resources[key];

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (_isAdjustingPosition || _isAnimating) return;

            if (args.DidSizeChange && _isAnchored)
            {
                UpdateShadowPolicy();
                SaveWindowState();
            }
            else if (args.DidPositionChange && _appWindow.IsVisible)
            {
                SaveWindowState();
            }
        }

        private void SaveWindowState()
        {
            if (!_appWindow.IsVisible || _isHiding) return;

            var state = WindowStateService.Instance.GetState(WindowKey) ?? new Persistence.Models.WindowState();
            state.X = _appWindow.Position.X;
            state.Y = _appWindow.Position.Y;
            state.WasOpen = false;

            // the size is derived on every open, never persisted; zeroed so no stale size survives
            state.Width = 0;
            state.Height = 0;

            WindowStateService.Instance.SetState(WindowKey, state);
        }

        // pushes the scroll mode onto the unnamed items panel, once the first layout pass has created it
        private void OnGraphsItemsControlLayoutUpdated(object? sender, object e)
        {
            if (GraphsItemsControl.ItemsPanelRoot is FluentSensors.Controls.VerticalStretchPanel panel)
            {
                _graphsPanel = panel;
                ApplyGraphsScrollMode(_isGraphsScrolling);
                GraphsItemsControl.LayoutUpdated -= OnGraphsItemsControlLayoutUpdated;
            }
        }

        // fills the window, or stacks fixed-height slots in a scroll region (a ScrollViewer measures with infinite
        // height, an equal split has nothing to divide); two explicit modes, so a one pixel rounding difference
        // cannot put a scrollbar on a window that fits
        private void ApplyGraphsScrollMode(bool isScrolling)
        {
            _isGraphsScrolling = isScrolling;

            if (GraphsScrollViewer != null)
            {
                GraphsScrollViewer.VerticalScrollMode = isScrolling ? ScrollMode.Enabled : ScrollMode.Disabled;
                GraphsScrollViewer.VerticalScrollBarVisibility = isScrolling
                    ? ScrollBarVisibility.Auto
                    : ScrollBarVisibility.Disabled;
            }

            if (_graphsPanel != null)
            {
                _graphsPanel.FixedItemHeight = isScrolling ? FlyoutGraphHeightDip : 0;
            }
        }

        private void SetGraphsRenderingActive(bool active)
        {
            if (KeepFlyoutGraphsActiveInBackground && !active) return;

            if (this.Content is DependencyObject root)
            {
                SensorGraphRenderingGate.SetActive(root, active);
            }
        }


        // === user interaction & dismiss ===

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            // always input active; a light-dismiss flyout is deactivated the moment focus leaves it, which would drop
            // the blur while it is still on screen
            if (_configurationSource != null)
            {
                _configurationSource.IsInputActive = true;
            }

            // light dismiss
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                // a click on the taskbar widget is left to its own toggle
                if (TaskbarWidgetWindow.CurrentInstance != null)
                {
                    var widgetHwnd = WinRT.Interop.WindowNative.GetWindowHandle(TaskbarWidgetWindow.CurrentInstance);
                    NativeMethods.GetWindowRect(widgetHwnd, out var widgetRect);
                    NativeMethods.GetCursorPos(out var cursorPos);

                    if (cursorPos.X >= widgetRect.Left && cursorPos.X <= widgetRect.Right &&
                        cursorPos.Y >= widgetRect.Top && cursorPos.Y <= widgetRect.Bottom)
                    {
                        return;
                    }
                }

                HideFlyout();
            }
            else if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                EnsureBehindTaskbarZOrder();
            }
        }

        // opens the sensor list on the taskbar profile, the one thing the flyout cannot do itself
        private void BackToDashboard_Click(object sender, RoutedEventArgs e)
        {
            HideFlyout();

            if (MainWindow.CurrentInstance != null)
            {
                MainWindow.CurrentInstance.OpenSensorsForProfile(SensorSelectionProfile.Taskbar);
            }
            else
            {
                var newMainWindow = new MainWindow();
                newMainWindow.Activate();
                newMainWindow.OpenSensorsForProfile(SensorSelectionProfile.Taskbar);
            }
        }

        private void TaskbarSettings_Click(object sender, RoutedEventArgs e)
        {
            HideFlyout();

            if (MainWindow.CurrentInstance != null)
            {
                MainWindow.CurrentInstance.OpenTaskbarSettings();
            }
            else
            {
                var newMainWindow = new MainWindow();
                newMainWindow.Activate();
                newMainWindow.OpenTaskbarSettings();
            }
        }

        // the snapshot; the flyout graphs stand still, the taskbar graphs run on
        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SetFlyoutPaused(!ViewModel.IsFlyoutPaused);
            ApplyPauseState();
        }

        // glyph, tooltip and name follow the state, like the csv logger pause button
        private void ApplyPauseState()
        {
            string label = ViewModel.IsFlyoutPaused ? "Resume" : "Pause";
            PauseButtonIcon.Glyph = ViewModel.IsFlyoutPaused ? "\uE768" : "\uE769";
            ToolTipService.SetToolTip(PauseButton, label);
            AutomationProperties.SetName(PauseButton, label);
        }

        // [Ctrl]+[Alt]+[S]: a bordered key per part, a plain plus between them
        private void ApplyShortcutHint()
        {
            var keys = FlyoutShortcutRegistration.RegisteredKeys;
            ShortcutHintPanel.Children.Clear();
            ShortcutHintPanel.Visibility = keys != null ? Visibility.Visible : Visibility.Collapsed;
            if (keys == null) return;

            var borderStyle = (Style)ShortcutHintPanel.Resources["ShortcutKeyBorderStyle"];
            var textStyle = (Style)ShortcutHintPanel.Resources["ShortcutKeyTextStyle"];
            var plusStyle = (Style)ShortcutHintPanel.Resources["ShortcutKeyPlusTextStyle"];

            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) ShortcutHintPanel.Children.Add(new TextBlock { Style = plusStyle, Text = "+" });

                ShortcutHintPanel.Children.Add(new Border
                {
                    Style = borderStyle,
                    Child = new TextBlock { Style = textStyle, Text = keys[i] }
                });
            }
        }

        private void OnShortcutRegistrationChanged()
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosed) return;
                ApplyShortcutHint();
            });
        }

        private void OnTaskbarTimeRangePicked(DependencyObject sender, DependencyProperty dp)
        {
            SettingsService.Instance.TaskbarGraphTimeSpanSeconds = TaskbarTimeRangePicker.SelectedSeconds;
        }

        private void OnFlyoutTimeRangePicked(DependencyObject sender, DependencyProperty dp)
        {
            SettingsService.Instance.TaskbarFlyoutGraphTimeSpanSeconds = FlyoutTimeRangePicker.SelectedSeconds;
        }

        // --- memory leak: TaskbarFlyoutWindow never released after close ---
        // problem: WinUI 3 never releases a closed secondary Window (confirmed, still open, even with empty content):
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // fix: hide instead of closing and keep the instance as _retainedInstance (same as
        // WidgetWindow and TaskbarWidgetWindow)
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // SafeDestroy is closing for real; a closed window handed back as _retainedInstance would leave the flyout
            // unable to repaint for the rest of the session
            if (_isClosed) return;

            args.Cancel = true;
            HideFlyout();
            CurrentInstance = null;
            _retainedInstance = this;
        }


        // === theme and backdrop ===

        // an in-app theme switch is a plain repaint through ApplyTheme, never a rebuild (that would tear down
        // the live instance mid-switch)
        private void OnThemeChanged(string newTheme)
        {
            this.DispatcherQueue.TryEnqueue(() => ApplyTheme(newTheme));
        }

        private void OnBackdropTypeChanged(string newType)
        {
            this.DispatcherQueue.TryEnqueue(() => SetBackdrop(newType));
        }

        private void OnOpacityChanged(float tintOpacity, float luminosityOpacity)
        {
            this.DispatcherQueue.TryEnqueue(() => UpdateAcrylicProperties());
        }

        private void OnTintColorChanged(bool useAccentColor, Windows.UI.Color customColor)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                UpdateAcrylicProperties();
                UpdateSolidBackground();
            });
        }

        // re-anchors for the next open (the flyout is usually hidden when the setting changes)
        private void OnFlyoutAlignmentChanged(string newAlignment)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosed || TaskbarWidgetWindow.CurrentInstance == null) return;
                PositionNextToTaskbar(TaskbarWidgetWindow.CurrentInstance, startForSlideAnimation: false);
            });
        }

        // a new slot height resizes an open flyout in place
        private void OnFlyoutGraphHeightChanged(double newHeightDip)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosed || TaskbarWidgetWindow.CurrentInstance == null) return;
                PositionNextToTaskbar(TaskbarWidgetWindow.CurrentInstance, startForSlideAnimation: false);
            });
        }

        // also on a taskbar move, the active edge has its own range
        private void OnTaskbarGraphTimeSpanChanged(double newTimeSpanSeconds)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosed) return;
                TaskbarTimeRangePicker.SelectedSeconds = newTimeSpanSeconds;
            });
        }

        private void OnFlyoutGraphTimeSpanChanged(double newTimeSpanSeconds)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosed) return;
                FlyoutTimeRangePicker.SelectedSeconds = newTimeSpanSeconds;
            });
        }

        private void ApplyTheme(string themeTag)
        {
            if (_isClosed) return;

            var elementTheme = themeTag switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = elementTheme;
            }

            if (FlyoutRootBorder != null)
            {
                FlyoutRootBorder.RequestedTheme = elementTheme;
            }

            SetConfigurationSourceTheme();
            UpdateAcrylicProperties();
            UpdateSolidBackground();

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

        private bool IsCurrentThemeLight()
        {
            if (_isClosed) return false;

            string themeTag = SettingsService.Instance.AppTheme;
            if (themeTag == "Light") return true;
            if (themeTag == "Dark") return false;

            try
            {
                return this.Content is FrameworkElement fe && fe.ActualTheme == ElementTheme.Light;
            }
            catch
            {
                return false;
            }
        }

        private void UpdateAcrylicProperties()
        {
            if (_isClosed) return;

            if (_acrylicController != null)
            {
                bool isLight = IsCurrentThemeLight();
                string backdropType = SettingsService.Instance.TaskbarBackdropType;

                if (backdropType == "Mica")
                {
                    // mica preset
                    if (isLight)
                    {
                        _acrylicController.TintColor = MicaPresetLightTintColor;
                        _acrylicController.LuminosityOpacity = MicaPresetLightLuminosity;
                        _acrylicController.FallbackColor = MicaPresetLightTintColor;
                    }
                    else
                    {
                        _acrylicController.TintColor = MicaPresetDarkTintColor;
                        _acrylicController.LuminosityOpacity = MicaPresetDarkLuminosity;
                        _acrylicController.FallbackColor = MicaPresetDarkTintColor;
                    }
                }
                else
                {
                    // "Acrylic" follows the settings sliders
                    // fallback tint when no accent or custom color applies (matches the opaque path)
                    Windows.UI.Color defaultTint = isLight
                        ? Windows.UI.Color.FromArgb(255, 0xED, 0xED, 0xED)
                        : Windows.UI.Color.FromArgb(255, 0x22, 0x22, 0x22);

                    Windows.UI.Color targetColor = SettingsService.Instance.TaskbarUseAccentColor
                        ? (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]
                        : (SettingsService.Instance.TaskbarCustomTintColor.A > 0 ? SettingsService.Instance.TaskbarCustomTintColor : defaultTint);

                    _acrylicController.TintColor = targetColor;
                    _acrylicController.TintOpacity = SettingsService.Instance.TaskbarTintOpacity;
                    _acrylicController.LuminosityOpacity = SettingsService.Instance.TaskbarLuminosityOpacity;
                    _acrylicController.FallbackColor = defaultTint;
                }
            }
        }

        // paints every flyout surface for the current backdrop mode, three exclusive cases in this order:
        // 1. a backdrop controller is attached; root and bar transparent, the graphs area keeps its translucent lift
        // 2. material "None" (Solid); the root takes the users accent or custom color, the graphs area the same lift
        // 3. otherwise (Mica/Acrylic with Windows transparency off); every surface its own flat opaque color, no
        //    overlay, so each one can be calibrated on its own
        // colors come from the App.xaml theme dictionary; (the XAML {ThemeResource} is only the first paint, a local
        // value from here outranks it)
        private void UpdateSolidBackground()
        {
            if (_isClosed || FlyoutRootBorder == null) return;

            bool isLight = IsCurrentThemeLight();
            var themeDictionary = (ResourceDictionary)Application.Current.Resources
                .ThemeDictionaries[isLight ? "Light" : "Default"];

            var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            var graphsOverlay = (Microsoft.UI.Xaml.Media.Brush)themeDictionary["FlyoutGraphsBackground"];

            bool onGlass = _acrylicController != null;

            if (onGlass)
            {
                FlyoutRootBorder.Background = transparent;
                FlyoutBottomBarBorder.Background = transparent;
                GraphsContentGrid.Background = graphsOverlay;
            }
            else if (SettingsService.Instance.TaskbarBackdropType == "None")
            {
                Windows.UI.Color targetColor = SettingsService.Instance.TaskbarUseAccentColor
                    ? (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]
                    : SettingsService.Instance.TaskbarCustomTintColor;

                FlyoutRootBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(targetColor);
                FlyoutBottomBarBorder.Background = transparent;
                GraphsContentGrid.Background = graphsOverlay;
            }
            else
            {
                FlyoutRootBorder.Background = (Microsoft.UI.Xaml.Media.Brush)themeDictionary["FlyoutWindowBackground"];
                FlyoutBottomBarBorder.Background = (Microsoft.UI.Xaml.Media.Brush)themeDictionary["FlyoutBottomBarBackground"];
                GraphsContentGrid.Background = transparent;
            }

            // the window stroke is opaque in every mode, the separator only off glass (an opaque line on glass stands
            // still while the material around it moves)
            FlyoutRootBorder.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)themeDictionary["FlyoutWindowBorderBrush"];
            FlyoutBottomBarBorder.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)themeDictionary[
                onGlass ? "FlyoutBottomBarSeparatorOnGlassBrush" : "FlyoutBottomBarSeparatorBrush"];

            // the grain belongs to the material, so it shows in the blur modes only
            FlyoutNoiseHost.Visibility = onGlass ? Visibility.Visible : Visibility.Collapsed;
            if (onGlass)
            {
                EnsureNoiseBitmap(FlyoutRootBorder.ActualWidth, FlyoutRootBorder.ActualHeight);
            }
        }

        // for a pure OS accent change; both calls resolve the accent fresh, no rebuild needed
        private void RefreshAccentSurfaces()
        {
            UpdateAcrylicProperties();
            UpdateSolidBackground();
        }

        // === acrylic grain ===

        // the acrylic recipe ends with a 2 percent noise layer (sc_noiseOpacity) that the backdrop controller does
        // not draw; painted here by hand, one random grayscale bitmap under every surface fill:
        // https://github.com/microsoft/microsoft-ui-xaml/blob/6aed8d97fdecfe9b19d70c36bd1dacd9c6add7c1/dev/Materials/Acrylic/AcrylicBrush.h
        private const int NoiseSeed = 0x5EED;

        // opacity stays the recipe constant, the strength is the value range around a mean of 128; (so tuning the
        // grain never moves the calibrated colors)
        private const double NoiseLayerOpacity = 0.02;
        private const double NoiseSpreadLevels = 3.5;

        private Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap? _noiseBitmap;
        private double _noiseScale;

        // grows on demand, never shrinks; sized in physical pixels and scaled back down, so one noise pixel lands on
        // one physical pixel instead of being smeared into coarse grain
        private void EnsureNoiseBitmap(double widthDip, double heightDip)
        {
            if (_isClosed || widthDip <= 0 || heightDip <= 0) return;

            double scale = FlyoutRootBorder.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0) scale = 1.0;

            int width = (int)Math.Ceiling(widthDip * scale);
            int height = (int)Math.Ceiling(heightDip * scale);

            bool scaleUnchanged = Math.Abs(scale - _noiseScale) < 0.001;
            if (_noiseBitmap != null && scaleUnchanged
                && _noiseBitmap.PixelWidth >= width && _noiseBitmap.PixelHeight >= height)
            {
                return;
            }

            if (scaleUnchanged)
            {
                width = Math.Max(width, _noiseBitmap?.PixelWidth ?? 0);
                height = Math.Max(height, _noiseBitmap?.PixelHeight ?? 0);
            }

            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(width, height);
            var random = new Random(NoiseSeed);
            var pixels = new byte[width * height * 4];

            int half = (int)Math.Round(NoiseSpreadLevels / (2.0 * NoiseLayerOpacity));

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte level = (byte)(128 - half + random.Next((half * 2) + 1));
                pixels[i] = level;
                pixels[i + 1] = level;
                pixels[i + 2] = level;
                pixels[i + 3] = 255;
            }

            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(pixels, 0, pixels.Length);
            }
            bitmap.Invalidate();

            _noiseBitmap = bitmap;
            _noiseScale = scale;

            FlyoutNoiseHost.Opacity = NoiseLayerOpacity;
            FlyoutNoiseOverlay.Width = width;
            FlyoutNoiseOverlay.Height = height;
            FlyoutNoiseOverlay.RenderTransform = new Microsoft.UI.Xaml.Media.ScaleTransform
            {
                ScaleX = 1.0 / scale,
                ScaleY = 1.0 / scale
            };

            // Stretch None; any stretching smears the grain
            FlyoutNoiseOverlay.Fill = new Microsoft.UI.Xaml.Media.ImageBrush
            {
                ImageSource = bitmap,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.None,
                AlignmentX = Microsoft.UI.Xaml.Media.AlignmentX.Left,
                AlignmentY = Microsoft.UI.Xaml.Media.AlignmentY.Top
            };
        }

        private void FlyoutRootBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isClosed || FlyoutNoiseHost.Visibility != Visibility.Visible) return;

            EnsureNoiseBitmap(e.NewSize.Width, e.NewSize.Height);
        }

        // applies the backdrop for the current setting and the Windows transparency state
        // "Mica" runs through DesktopAcrylicController too, with the MicaPreset constants instead of the sliders: real
        // Mica only samples the wallpaper and shows next to nothing on a small flyout (WidgetWindow uses a real
        // MicaController for the same setting)
        public void SetBackdrop(string backdropType)
        {
            if (_isClosed) return;
            DispatcherQueue.EnsureSystemDispatcherQueue();

            bool isTransparencyEnabled = _uiSettings != null && _uiSettings.AdvancedEffectsEnabled;

            if (_configurationSource == null)
            {
                _configurationSource = new SystemBackdropConfiguration();
                this.Activated += (s, e) => { if (_configurationSource != null) _configurationSource.IsInputActive = true; };
                _configurationSource.IsInputActive = true;
            }

            SetConfigurationSourceTheme();

            _acrylicController?.Dispose();
            _acrylicController = null;
            this.SystemBackdrop = null;

            if (isTransparencyEnabled && (backdropType == "Acrylic" || backdropType == "Mica") && DesktopAcrylicController.IsSupported())
            {
                _acrylicController = new DesktopAcrylicController();
                // Base is the variant the Windows 11 shell surfaces use:
                // https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.systembackdrops.desktopacrylickind
                _acrylicController.Kind = DesktopAcrylicKind.Base;
                _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
                _acrylicController.SetSystemBackdropConfiguration(_configurationSource);

                UpdateAcrylicProperties();
            }
            else
            {
                this.SystemBackdrop = new TransparentTintBackdrop();
            }

            // the base color, read off the controller set above
            UpdateSolidBackground();

            UpdateShadowPolicy();
        }

        private void SetConfigurationSourceTheme()
        {
            if (_isClosed || _configurationSource == null) return;

            _configurationSource.Theme = IsCurrentThemeLight()
                ? SystemBackdropTheme.Light
                : SystemBackdropTheme.Dark;
        }

        // --- workaround: DWM backdrop swapchain kick ---
        // problem: after a Windows transparency or theme change, DesktopAcrylicController needs a rebind to attach its
        // blur to the new DWM swapchain
        // fix: after a rebuild, kick the backdrop once (None, then the current one), with parameters only
        private void KickBackdropRefresh()
        {
            if (_isClosed) return;

            string currentBackdrop = SettingsService.Instance.TaskbarBackdropType;
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
