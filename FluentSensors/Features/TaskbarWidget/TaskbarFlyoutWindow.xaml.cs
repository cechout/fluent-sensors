using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Controls.SensorRow;
using FluentSensors.Core.Taskbar;
using FluentSensors.Persistence.Models;
using FluentSensors.Persistence.Services;
using FluentSensors.Features.Widget;


namespace FluentSensors.Features.TaskbarWidget
{
    // companion flyout window displaying live telemetry graphs directly anchored above the taskbar widget
    //
    // WinUI 3 has no built-in support for anchoring a borderless, non-activating window to an external Win32 shell
    // window; this window combines several low-level techniques:
    // 1. eliminates the non-client titlebar stripe via WM_NCCALCSIZE (0x0083) returning 0
    // 2. caps its own height against the work area and scrolls the graph list rather than growing past it
    // 3. places the window directly beneath Shell_TrayWnd in Z-order so it slides out from under the taskbar
    // 4. coordinates a physical window slide via DispatcherTimer with direct composition opacity fading
    // 5. applies dynamic DWM corner preferences and shadow suppression depending on the Windows transparency setting
    // 6. integrates DesktopAcrylicController / Mica system backdrop with a swapchain kick on theme changes
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

        // physical window slide distance in DIP/pixels
        public const int WindowSlideDistanceDip = 300;

        // animation duration in milliseconds
        public const int EnterAnimationDurationMs = 260; // duration on open (move-in)
        public const int ExitAnimationDurationMs = 140;  // duration on close (move-out)

        // how far the two durations and the slide distance above follow the pinned sensor count: all three are
        // tuned for AnimationReferenceSensorCount sensors, every sensor above or below scales them by their own factor
        // these three are the knobs to turn; 0 pins a value to its base, 0.18 makes a three sensor flyout 18 percent
        // slower than a two sensor one
        public const int AnimationReferenceSensorCount = 2;
        public const double EnterDurationPerSensorFactor = 0.20; // scaling per sensor on open
        public const double ExitDurationPerSensorFactor = 0.14;  // scaling per sensor on close
        public const double SlideDistancePerSensorFactor = 0.08; // scaling per sensor on the travelled distance

        // keeps a single sensor from snapping open and a full height flyout from crawling
        public const double MinAnimationScaleFactor = 0.75;
        public const double MaxAnimationScaleFactor = 3.0;

        // fade opacity (1.0f = no fade, 0.0f = full fade)
        public const float EnterFadeStartOpacity = 0.9f; // initial opacity when opening
        public const float EnterFadeEndOpacity = 1.0f; // target opacity when opening
        public const float ExitFadeEndOpacity = 0.9f; // target opacity when closing

        // background live graph rendering toggle
        // keeps graphs rendering continuously in background so they are instantly visible on open
        public const bool KeepFlyoutGraphsActiveInBackground = true;

        // --- Mica Flyout Blur Preset Settings (used when BackdropType == "Mica" and Transparency is ON) ---
        // over a flat backdrop the controller resolves to lerp(backdrop, tint, luminosity), so measuring one
        // surface over a dark and over a bright background yields both unknowns; run against the native shell
        // flyouts that gives 4 percent backdrop transmission in dark and 9 percent in light
        //
        // no TintOpacity here on purpose: the tint sits in a blend that carries hue and saturation only, so a
        // neutral gray tint turns that layer into a no-op and luminosity is the one knob that moves the result
        // the effect graph is public; note that the source flags its own BlendEffectMode names as swapped, so the
        // tint layer reads as Luminosity there while it behaves as a color blend:
        // https://github.com/microsoft/microsoft-ui-xaml/blob/6aed8d97fdecfe9b19d70c36bd1dacd9c6add7c1/dev/Materials/Acrylic/AcrylicBrush.cpp
        //
        // the tints are the Fluent acrylic base tones, AcrylicBackgroundFillColorBaseBrush:
        // https://github.com/microsoft/microsoft-ui-xaml/blob/6aed8d97fdecfe9b19d70c36bd1dacd9c6add7c1/dev/Materials/Acrylic/AcrylicBrush_19h1_themeresources.xaml
        // they are not pre-compensated the way the opaque literals are, because the render shift applies once to
        // the finished composite and is already contained in the measurements these were derived from
        // Dark Mode Preset:
        public static readonly Windows.UI.Color MicaPresetDarkTintColor = Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20);
        public const float MicaPresetDarkLuminosity = 0.96f;

        // Light Mode Preset:
        public static readonly Windows.UI.Color MicaPresetLightTintColor = Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3);
        public const float MicaPresetLightLuminosity = 0.91f;


        // === fields ===

        private const string WindowKey = "TaskbarFlyout";

        // Anchor offsets configurable in code-behind
        // the taskbar gap doubles as the gap to the top of the work area, and that pair is what caps the window
        // height, see PositionAboveTaskbar
        public const int FlyoutMarginToTaskbarDip = 12; // vertical gap between taskbar top and flyout bottom edge
        public const int FlyoutMarginToScreenEdgeDip = 12; // smallest gap the flyout keeps to the left and right screen edges

        // horizontal offset from the aligned edge, meaning depends on TaskbarFlyoutAlignment:
        // Left = pixels to move right (inward from the widget left edge)
        // Right = pixels to move left (inward from the widget right edge)
        // Center = unused
        public const int FlyoutHorizontalOffsetDip = 0;

        // fixed width in DIP (matching standard WidgetWindow width)
        public const int FlyoutDefaultWidthDip = 250;

        // height of a single graph slot in DIP
        public const int FlyoutDefaultGraphHeightDip = 100;

        // --- flyout layout ---

        // the two knobs for the interior; every visible inset inside the window comes from one of them
        // the graphs inset is a margin on the list rather than padding on the surface below it, so the scroll region
        // stays the full width of the window and the scrollbar rides its edge instead of sitting 6px inside
        public static readonly Thickness FlyoutGraphsMargin = new Thickness(6, 9, 6, 7);
        public static readonly Thickness FlyoutBottomBarPadding = new Thickness(0, 0, 0, 0);

        // gap between two stacked graphs
        public const double FlyoutGraphSpacingDip = 8;

        // (AppBarButton renders at the platform AppBarThemeCompactHeight; mirrored here because the window height
        // math runs long before the bar is ever measured)
        public const double FlyoutBottomBarButtonHeightDip = 48;

        // separator drawn as the top border of FlyoutBottomBarBorder
        private const double FlyoutBottomBarSeparatorDip = 1;

        // bottom action bar strip, added on top of the graph slots in the window height math; derived, so
        // changing FlyoutBottomBarPadding corrects that math on its own
        private static double FlyoutBottomBarHeightDip =>
            FlyoutBottomBarSeparatorDip + FlyoutBottomBarPadding.Top
            + FlyoutBottomBarButtonHeightDip + FlyoutBottomBarPadding.Bottom;

        private AppWindow _appWindow;
        private IntPtr _hwnd;
        private WindowMessageMonitor _messageMonitor;
        private UISettings? _uiSettings;

        private int _bottomAnchorY;
        private int _targetX;
        private int _targetY;
        private bool _isAdjustingPosition;
        private bool _isHiding;

        // sensor count the slide durations are scaled by; capped at what still fits once the window reaches its
        // height cap, because past that point the window stops growing
        private int _animationSensorCount = AnimationReferenceSensorCount;

        // the graph rows sit in an ItemsPanelTemplate and cannot be named in XAML, so the panel is cached the first
        // time a layout pass produces it; _isGraphsScrolling holds the mode until then
        private FluentSensors.Controls.VerticalStretchPanel? _graphsPanel;
        private bool _isGraphsScrolling;

        // native window animation timer
        private DispatcherTimer? _animTimer;
        private Stopwatch? _animStopwatch;
        private int _animStartX, _animStartY, _animTargetX, _animTargetY;
        private Action? _animOnComplete;

        public TaskbarWidgetViewModel ViewModel { get; }
        public static TaskbarFlyoutWindow? CurrentInstance { get; private set; }
        private static TaskbarFlyoutWindow? _retainedInstance;

        // system backdrop controllers
        // no MicaController here on purpose: material "Mica" is served by _acrylicController too, see SetBackdrop
        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _configurationSource;


        // === constructor ===

        public TaskbarFlyoutWindow(TaskbarWidgetViewModel viewModel)
        {
            ViewModel = viewModel;
            this.InitializeComponent();
            CurrentInstance = this;

            _appWindow = this.AppWindow;
            _appWindow.IsShownInSwitchers = false;
            _appWindow.SetIcon("Assets\\Icon\\Icon.ico");
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            // frameless presenter without default OS caption buttons (no top-right close/min/max)
            var presenter = OverlappedPresenter.Create();
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = false; // do not force topmost above taskbar shell
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false; // the height is always derived from the pinned sensor count, never dragged
            _appWindow.SetPresenter(presenter);

            // remove WS_THICKFRAME and WS_CAPTION and apply WS_POPUP and WS_EX_TOOLWINDOW
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

            // hook win32 messages:
            // 1. WM_NCCALCSIZE (0x0083): eliminates the 8px non-client white titlebar stripe completely
            // 2. WM_SYSCOMMAND (0x0112): prevents dragging/moving the window
            _messageMonitor = new WindowMessageMonitor(_hwnd);
            _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;

            // initialize shadow policy & UISettings based on Windows transparency setting
            InitializeShadowPolicy();

            // theming & backdrop (Taskbar ecosystem)
            SetBackdrop(SettingsService.Instance.TaskbarBackdropType);
            ApplyTheme(SettingsService.Instance.AppTheme);

            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            SettingsService.Instance.TaskbarBackdropTypeChanged += OnBackdropTypeChanged;
            SettingsService.Instance.TaskbarOpacityChanged += OnOpacityChanged;
            SettingsService.Instance.TaskbarTintColorChanged += OnTintColorChanged;
            SettingsService.Instance.TaskbarFlyoutAlignmentChanged += OnFlyoutAlignmentChanged;

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

            // the interior is driven entirely from the layout metrics above, the XAML carries no numbers of its own
            GraphsItemsControl.Margin = FlyoutGraphsMargin;
            BottomBarContentGrid.Padding = FlyoutBottomBarPadding;
            GraphsItemsControl.LayoutUpdated += OnGraphsItemsControlLayoutUpdated;

            _appWindow.Changed += AppWindow_Changed;
            FlyoutRootBorder.SizeChanged += FlyoutRootBorder_SizeChanged;
            _appWindow.Closing += AppWindow_Closing;
            this.Activated += Window_Activated;

            KickBackdropRefresh();
        }


        // === shadow policy (based on Windows Transparency Effects) ===

        private void InitializeShadowPolicy()
        {
            try
            {
                _uiSettings = new UISettings();
                _uiSettings.AdvancedEffectsEnabledChanged += OnSystemVisualSettingsChanged;
                _uiSettings.ColorValuesChanged += OnSystemVisualSettingsChanged;
                UpdateShadowPolicy();
            }
            catch
            {
                // safety guard if UISettings is unavailable
            }
        }

        // a named handler rather than a lambda, so SafeDestroy can detach it again; every rebuilt window subscribes
        // anew and without the detach the UISettings handler list grows by one per rebuild
        private void OnSystemVisualSettingsChanged(UISettings sender, object args)
        {
            ScheduleRecreation();
        }

        private void UpdateShadowPolicy()
        {
            if (_hwnd == IntPtr.Zero) return;

            bool isTransparencyEnabled = _uiSettings != null && _uiSettings.AdvancedEffectsEnabled;

            if (isTransparencyEnabled)
            {
                // Transparency ON: Native Windows 11 rounded window corners (8px) with GPU DWM clipping (eliminates black box)
                int cornerPreference = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
                DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

                var margins = new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 1, cyBottomHeight = 0 };
                DwmExtendFrameIntoClientArea(_hwnd, ref margins);

                int policy = (int)DWMNCRENDERINGPOLICY.DWMNCRP_DISABLED;
                DwmSetWindowAttribute(_hwnd, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));
            }
            else
            {
                // Transparency OFF: 100% transparent HWND with zero DWM drop shadow and XAML 8px rounded solid canvas
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
                    // 0 = client area covers 100% of the window rectangle, eliminating the 8px top border gap
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

        // recalculates the flyout geometry so the next open reflects the current pinned sensor count
        public static void ResetGeometry()
        {
            if (CurrentInstance != null && TaskbarWidgetWindow.CurrentInstance != null)
            {
                CurrentInstance.PositionAboveTaskbar(TaskbarWidgetWindow.CurrentInstance, startForSlideAnimation: false);
            }
            else if (_retainedInstance != null && TaskbarWidgetWindow.CurrentInstance != null)
            {
                _retainedInstance.PositionAboveTaskbar(TaskbarWidgetWindow.CurrentInstance, startForSlideAnimation: false);
            }
        }

        // preloads the flyout instance into memory at taskbar initialization to eliminate first-open latency
        public static void Preload(TaskbarWidgetWindow widgetWindow)
        {
            // a retained instance is assumed to be a live window; if a destroyed one ever sits there this silently
            // skips the rebuild and the flyout stays dead for the rest of the session, see AppWindow_Closing
            if (widgetWindow == null || CurrentInstance != null || _retainedInstance != null) return;

            var window = new TaskbarFlyoutWindow(widgetWindow.ViewModel);
            _retainedInstance = window;
            window.PositionAboveTaskbar(widgetWindow, startForSlideAnimation: false);

            if (KeepFlyoutGraphsActiveInBackground)
            {
                window.SetGraphsRenderingActive(true);
            }
        }

        // ensures the flyout is placed directly behind the taskbar shell in Z-order
        private void EnsureBehindTaskbarZOrder()
        {
            var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd != IntPtr.Zero)
            {
                // place behind taskbar: SWP_NOSIZE (0x1) | SWP_NOMOVE (0x2) | SWP_NOACTIVATE (0x10) | SWP_NOOWNERZORDER (0x200)
                SetWindowPos(_hwnd, taskbarHwnd, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
            }
        }

        // toggles visibility of the flyout directly above the taskbar widget
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
                CurrentInstance.PositionAboveTaskbar(widgetWindow, startForSlideAnimation: true);
                CurrentInstance.SetGraphsRenderingActive(true);
                CurrentInstance._appWindow.Show();
                CurrentInstance.EnsureBehindTaskbarZOrder();
                CurrentInstance.Activate();
                CurrentInstance.SlideInFromBottom();
                return;
            }

            if (_retainedInstance != null)
            {
                var window = _retainedInstance;
                _retainedInstance = null;
                CurrentInstance = window;

                window.ApplyTheme(SettingsService.Instance.AppTheme);
                window.PositionAboveTaskbar(widgetWindow, startForSlideAnimation: true);
                window.SetGraphsRenderingActive(true);
                window._appWindow.Show();
                window.EnsureBehindTaskbarZOrder();
                window.Activate();
                window.SlideInFromBottom();
                return;
            }

            var newWindow = new TaskbarFlyoutWindow(widgetWindow.ViewModel);
            newWindow.ApplyTheme(SettingsService.Instance.AppTheme);
            newWindow.PositionAboveTaskbar(widgetWindow, startForSlideAnimation: true);
            newWindow.SetGraphsRenderingActive(true);
            newWindow._appWindow.Show();
            newWindow.EnsureBehindTaskbarZOrder();
            newWindow.Activate();
            newWindow.SlideInFromBottom();
        }

        public void HideFlyout()
        {
            if (_isHiding || !_appWindow.IsVisible) return;
            _isHiding = true;

            SlideOutToBottom(() =>
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
        // problem: WinUI 3 never releases secondary Window objects back to the GC/OS after a real close
        // confirmed, still-open platform bug, reproducible even with empty window content:
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // everywhere else in this project the answer is hide-and-reuse (_retainedInstance); this method is the one
        // place that deliberately does the opposite and destroys the window for real, because DWM does not rebind
        // DesktopAcrylicController to a fresh swapchain without a full recreation, see ScheduleRecreation below
        // price: one leaked CCW per OS theme or transparency change, knowingly paid, because the alternative was a
        // flyout that silently stopped repainting and switching theme for the rest of the session
        //
        // only ever call this from ExecuteFullRebuild, never from the normal hide path
        public void SafeDestroy()
        {
            if (_isClosed) return;
            _isClosed = true;

            try
            {
                SettingsService.Instance.ThemeChanged -= OnThemeChanged;
                SettingsService.Instance.TaskbarBackdropTypeChanged -= OnBackdropTypeChanged;
                SettingsService.Instance.TaskbarOpacityChanged -= OnOpacityChanged;
                SettingsService.Instance.TaskbarTintColorChanged -= OnTintColorChanged;
                SettingsService.Instance.TaskbarFlyoutAlignmentChanged -= OnFlyoutAlignmentChanged;
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

            // AppWindow_Closing cancels the close and re-registers this instance as _retainedInstance; detaching it
            // here is what lets the Close below go through instead of resurrecting a window that is already torn down
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

        // --- workaround: window recreation on global OS theme/transparency change ---
        // problem: the exact underlying reason why Windows DWM fails to bind DesktopAcrylicController blur properly
        // without a complete window recreation is not fully clear and is based purely on empirical observation
        // fix: upon receiving a global theme or transparency change from Windows, all three windows
        // (TaskbarFlyoutWindow, WidgetWindow and TaskbarWidgetWindow, each if open) are fully destroyed and rebuilt;
        // each rebuilt window then kicks its own backdrop from its constructor via KickBackdropRefresh, which goes
        // through SetBackdrop parameters only
        //
        // only ever driven by the Windows-level UISettings events; an in-app theme switch must not land here, and a
        // persisted SettingsService property must never be toggled to force a repaint: every setter snapshots the whole
        // settings object into the debounced writer, so an interrupted toggle persists its intermediate value
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

        // tears both flyout instances down for real and rebuilds all three windows, the destructive half of the
        // workaround documented on ScheduleRecreation above
        //
        // the only caller of SafeDestroy; the flyout goes first and comes back last, because it anchors its geometry
        // to the taskbar widget and the new widget has to sit on the taskbar before the new flyout can be placed
        private static void ExecuteFullRebuild()
        {
            bool flyoutWasVisible = CurrentInstance != null && CurrentInstance._appWindow != null && CurrentInstance._appWindow.IsVisible;

            // 1. Safely destroy existing TaskbarFlyoutWindow instances
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

            // 2. Safely destroy and rebuild WidgetWindow if open
            WidgetWindow.RecreateWindow();

            // 3. Safely destroy and rebuild TaskbarWidgetWindow; the rebuilt widget brings the flyout back itself
            // from the end of its embedding step, see TaskbarWidgetWindow.RecreateWindow
            TaskbarWidgetWindow.RecreateWindow(restoreFlyout: flyoutWasVisible);
        }


        // === physical window slide & content fade animations ===

        private void SlideInFromBottom()
        {
            TaskbarWidgetWindow.CurrentInstance?.SetFlyoutActive(true);

            int durationMs = ScaleAnimationDuration(EnterAnimationDurationMs, EnterDurationPerSensorFactor);

            // 1. Content Fade (DirectComposition)
            PlayContentFade(EnterFadeStartOpacity, EnterFadeEndOpacity, durationMs);

            // 2. Physical Window Slide Up
            int slideDistPx = GetSlideDistancePx(GetScaleFactor());
            int startY = _targetY + slideDistPx;

            EnsureBehindTaskbarZOrder();
            AnimateNativeWindowPosition(_targetX, startY, _targetX, _targetY, durationMs, isEntering: true);
        }

        private void SlideOutToBottom(Action onCompleted)
        {
            TaskbarWidgetWindow.CurrentInstance?.SetFlyoutActive(false);

            int durationMs = ScaleAnimationDuration(ExitAnimationDurationMs, ExitDurationPerSensorFactor);

            // 1. Content Fade (DirectComposition)
            PlayContentFade(1.0f, ExitFadeEndOpacity, durationMs);

            // 2. Physical Window Slide Down
            int slideDistPx = GetSlideDistancePx(GetScaleFactor());
            int currentX = _appWindow.Position.X;
            int currentY = _appWindow.Position.Y;
            int endY = _targetY + slideDistPx;

            EnsureBehindTaskbarZOrder();
            AnimateNativeWindowPosition(currentX, currentY, currentX, endY, durationMs, isEntering: false, onComplete: onCompleted);
        }

        // the shared scaling law: how far a value moves from its base with the number of graph rows the flyout is
        // showing, so a tall window does not open in the time a two row one does
        private double AnimationScaleFactor(double perSensorFactor)
        {
            double factor = 1.0 + ((_animationSensorCount - AnimationReferenceSensorCount) * perSensorFactor);

            return Math.Clamp(factor, MinAnimationScaleFactor, MaxAnimationScaleFactor);
        }

        private int ScaleAnimationDuration(int baseDurationMs, double perSensorFactor)
        {
            return (int)Math.Round(baseDurationMs * AnimationScaleFactor(perSensorFactor));
        }

        // the distance both slides travel, in physical pixels
        // PositionAboveTaskbar parks the window on the same value before the enter slide, so it has to come from here
        // rather than from WindowSlideDistanceDip directly
        private int GetSlideDistancePx(double scaleFactor)
        {
            return (int)Math.Round(WindowSlideDistanceDip * AnimationScaleFactor(SlideDistancePerSensorFactor) * scaleFactor);
        }

        private void PlayContentFade(float fromOpacity, float toOpacity, int durationMs)
        {
            if (RootGrid == null) return;
            var visual = ElementCompositionPreview.GetElementVisual(RootGrid);
            var compositor = visual?.Compositor;
            if (compositor == null) return;

            // NO inner offset animation on RootGrid - only smooth opacity fade
            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.InsertKeyFrame(0.0f, fromOpacity);
            opacityAnim.InsertKeyFrame(1.0f, toOpacity);
            opacityAnim.Duration = TimeSpan.FromMilliseconds(durationMs);
            visual.StartAnimation("Opacity", opacityAnim);
        }

        private void AnimateNativeWindowPosition(int startX, int startY, int targetX, int targetY, int durationMs, bool isEntering, Action? onComplete = null)
        {
            _animTimer?.Stop();
            _animStartX = startX;
            _animStartY = startY;
            _animTargetX = targetX;
            _animTargetY = targetY;
            _animOnComplete = onComplete;

            _isAdjustingPosition = true;
            _appWindow.Move(new PointInt32(startX, startY));
            _isAdjustingPosition = false;

            _animStopwatch = Stopwatch.StartNew();
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
            _animTimer.Tick += (s, e) =>
            {
                if (_animStopwatch == null) return;

                double progress = Math.Clamp((double)_animStopwatch.ElapsedMilliseconds / durationMs, 0.0, 1.0);

                // Fluent 2 Easing curves:
                // Enter: Fast Out, Slow In (Cubic ease-out) = 1 - (1 - p)^3
                // Exit: Slow Out, Fast In (Cubic ease-in) = p^3
                double ease = isEntering
                    ? (1.0 - Math.Pow(1.0 - progress, 3))
                    : Math.Pow(progress, 3);

                int currentX = (int)Math.Round(_animStartX + ((_animTargetX - _animStartX) * ease));
                int currentY = (int)Math.Round(_animStartY + ((_animTargetY - _animStartY) * ease));

                _isAdjustingPosition = true;
                _appWindow.Move(new PointInt32(currentX, currentY));
                _isAdjustingPosition = false;

                if (progress >= 1.0)
                {
                    _animTimer?.Stop();
                    _animStopwatch?.Stop();
                    _animOnComplete?.Invoke();
                    _animOnComplete = null;
                }
            };
            _animTimer.Start();
        }


        // === window sizing and positioning ===

        // places the flyout horizontally over the taskbar widget per TaskbarFlyoutAlignment (centered, left or right)
        // and anchors its bottom edge just above the taskbar, then clamps the result so it never leaves the primary
        // work area
        private void PositionAboveTaskbar(TaskbarWidgetWindow widgetWindow, bool startForSlideAnimation = false)
        {
            var widgetHwnd = WinRT.Interop.WindowNative.GetWindowHandle(widgetWindow);
            NativeMethods.GetWindowRect(widgetHwnd, out var widgetRect);

            var primaryTaskbar = WinTaskbarService.Instance.DiscoverNow().FirstOrDefault();
            double scale = primaryTaskbar != null ? (primaryTaskbar.Dpi / 96.0) : GetScaleFactor();

            // width is fixed to FlyoutDefaultWidthDip (matching standard WidgetWindow width)
            int desiredWidthPx = (int)Math.Round(FlyoutDefaultWidthDip * scale);

            int sensorCount = ViewModel.PinnedSensors.Count;

            // vertical gap above the taskbar, horizontal placement over the widget per TaskbarFlyoutAlignment
            int marginPx = (int)Math.Round(FlyoutMarginToTaskbarDip * scale);
            int offsetPx = (int)Math.Round(FlyoutHorizontalOffsetDip * scale);
            int edgeMarginPx = (int)Math.Round(FlyoutMarginToScreenEdgeDip * scale);

            _bottomAnchorY = primaryTaskbar != null ? (primaryTaskbar.Rect.Y - marginPx) : (widgetRect.Top - marginPx);

            // the gap that holds the flyout off the taskbar is also the gap it keeps to the top of the work area, and
            // that pair caps the height; without the cap enough pinned sensors produce a window taller than the screen
            // whose bottom edge ends up behind the taskbar
            // one graph slot is the floor, so a taskbar on a very short work area cannot produce a zero height window
            var workArea = DisplayArea.Primary.WorkArea;
            int maxHeightPx = Math.Max(
                CalculateFlyoutDefaultHeight(1, scale),
                _bottomAnchorY - (workArea.Y + marginPx));

            int desiredHeightPx = CalculateFlyoutDefaultHeight(sensorCount, scale);
            bool isScrolling = desiredHeightPx > maxHeightPx;
            if (isScrolling)
            {
                desiredHeightPx = maxHeightPx;
            }

            // once capped the window stops growing, so the slide durations stop growing with it too
            _animationSensorCount = isScrolling ? CountFittingGraphSlots(maxHeightPx, scale) : sensorCount;
            ApplyGraphsScrollMode(isScrolling);

            // Left/Right anchor to the matching widget edge and let the offset pull the flyout inward;
            // Center ignores the offset and lines the flyout center up with the widget center
            _targetX = SettingsService.Instance.TaskbarFlyoutAlignment switch
            {
                "Left" => widgetRect.Left + offsetPx,
                "Right" => widgetRect.Right - desiredWidthPx - offsetPx,
                _ => ((widgetRect.Left + widgetRect.Right) / 2) - (desiredWidthPx / 2)
            };
            _targetY = _bottomAnchorY - desiredHeightPx;

            // clamp within the primary work area, never closer than edgeMarginPx to a left or right screen edge;
            // the left edge wins when the screen is too narrow to honor both sides at once
            // the top needs no clamp of its own, the height cap above already lands _targetY on marginPx
            int rightLimitX = workArea.X + workArea.Width - desiredWidthPx - edgeMarginPx;
            int leftLimitX = workArea.X + edgeMarginPx;
            _targetX = Math.Max(leftLimitX, Math.Min(_targetX, rightLimitX));

            int initialY = startForSlideAnimation
                ? (_targetY + GetSlideDistancePx(scale))
                : _targetY;

            _isAdjustingPosition = true;
            _appWindow.MoveAndResize(new RectInt32(_targetX, initialY, desiredWidthPx, desiredHeightPx));
            _isAdjustingPosition = false;

            UpdateShadowPolicy();
        }

        private double GetScaleFactor()
        {
            uint dpi = GetDpiForWindow(_hwnd);
            return dpi / 96.0;
        }

        private int CalculateFlyoutDefaultHeight(int sensorCount, double scaleFactor)
        {
            return (int)(CalculateFlyoutContentHeight(sensorCount, FlyoutDefaultGraphHeightDip) * scaleFactor);
        }

        // how many graph slots still fit inside a capped window height, at the standard slot height
        private static int CountFittingGraphSlots(int maxHeightPx, double scaleFactor)
        {
            double interiorDip = (maxHeightPx / scaleFactor) - FlyoutBottomBarHeightDip
                - FlyoutGraphsMargin.Top - FlyoutGraphsMargin.Bottom;

            int slots = (int)Math.Floor(
                (interiorDip + FlyoutGraphSpacingDip) / (FlyoutDefaultGraphHeightDip + FlyoutGraphSpacingDip));

            return Math.Max(1, slots);
        }

        // window height for n graph slots: the bar strip, the graphs padding, n slots and the n-1 gaps between them
        private static double CalculateFlyoutContentHeight(int sensorCount, double graphHeightDip)
        {
            if (sensorCount <= 0) return FlyoutBottomBarHeightDip;

            return FlyoutBottomBarHeightDip
                + FlyoutGraphsMargin.Top + FlyoutGraphsMargin.Bottom
                + (sensorCount * graphHeightDip)
                + ((sensorCount - 1) * FlyoutGraphSpacingDip);
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (_isAdjustingPosition || _animTimer?.IsEnabled == true) return;

            if (args.DidSizeChange && _bottomAnchorY > 0)
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

            // the size is no longer persisted at all, it is derived from the pinned sensor count and the height cap
            // on every open; clearing it here drops whatever a resizable flyout left behind
            state.Width = 0;
            state.Height = 0;

            WindowStateService.Instance.SetState(WindowKey, state);
        }

        // the graph panel sits inside an ItemsPanelTemplate and cannot be named, so FlyoutGraphSpacingDip and the
        // scroll mode are pushed onto it from here; the items host only exists after the first layout pass, hence
        // the retry
        private void OnGraphsItemsControlLayoutUpdated(object? sender, object e)
        {
            if (GraphsItemsControl.ItemsPanelRoot is FluentSensors.Controls.VerticalStretchPanel panel)
            {
                _graphsPanel = panel;
                panel.Spacing = FlyoutGraphSpacingDip;
                ApplyGraphsScrollMode(_isGraphsScrolling);
                GraphsItemsControl.LayoutUpdated -= OnGraphsItemsControlLayoutUpdated;
            }
        }

        // switches the graph list between filling the window and stacking at the standard slot height inside a scroll
        // region; the fixed height is what the panel needs there, a ScrollViewer measures with infinite height and an
        // equal split has nothing to divide
        //
        // two explicit modes rather than one permanently scrolling viewer, so a rounding difference of a single pixel
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
                _graphsPanel.FixedItemHeight = isScrolling ? FlyoutDefaultGraphHeightDip : 0;
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
            // deliberately always true, instead of the usual
            // IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated
            // a light-dismiss flyout counts as deactivated the moment focus leaves it, which would drop the blur while
            // the window is still on screen; same reasoning as WidgetWindow.Window_Activated
            if (_configurationSource != null)
            {
                _configurationSource.IsInputActive = true;
            }

            // light dismiss: when user clicks outside the flyout window, close it cleanly
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                // if click occurred on the taskbar widget, let the widget click handler handle toggling
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

        private void BackToDashboard_Click(object sender, RoutedEventArgs e)
        {
            HideFlyout();

            if (MainWindow.CurrentInstance != null)
            {
                MainWindow.CurrentInstance.OpenDashboard();
            }
            else
            {
                var newMainWindow = new MainWindow();
                newMainWindow.Activate();
            }
        }

        private void CloseWidget_Click(object sender, RoutedEventArgs e)
        {
            HideFlyout();
            TaskbarWidgetWindow.CurrentInstance?.CloseWidget();
        }

        // --- memory leak: TaskbarFlyoutWindow never released after close ---
        // problem: WinUI 3 never releases secondary Window objects back to the GC/OS after a real close
        // confirmed, still-open platform bug, reproducible even with empty window content:
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // fix: hide instead of actually closing, and keep this instance around (_retainedInstance) for reuse
        // same approach as WidgetWindow and TaskbarWidgetWindow
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // SafeDestroy is tearing this instance down: let the close proceed, and above all do not hand a window
            // with _isClosed set back to _retainedInstance, which makes Preload skip building a live one and leaves
            // the flyout permanently unable to repaint or switch theme
            if (_isClosed) return;

            args.Cancel = true;
            HideFlyout();
            CurrentInstance = null;
            _retainedInstance = this;
        }


        // === theme and backdrop application (Taskbar Ecosystem) ===

        // ApplyTheme already re-runs SetConfigurationSourceTheme, UpdateAcrylicProperties and UpdateSolidBackground,
        // so an in-app theme switch is a plain repaint; recreating the window here tore down the live instance
        // mid-switch, which is what produced the closed-window COMException
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

        // re-anchors the flyout when the alignment setting changes; the flyout is usually hidden at that point, so
        // this just refreshes the target for the next open, same as ResetGeometry does after a size reset
        private void OnFlyoutAlignmentChanged(string newAlignment)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosed || TaskbarWidgetWindow.CurrentInstance == null) return;
                PositionAboveTaskbar(TaskbarWidgetWindow.CurrentInstance, startForSlideAnimation: false);
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
                    // Use dedicated custom Mica blur preset
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
                    // "Acrylic" uses user-configured settings sliders
                    // fallback tint when no accent/custom color applies: #EDEDED Light, #222222 Dark (matches the opaque path)
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

        // paints every visible flyout surface for the current backdrop mode
        //
        // three mutually exclusive cases, in this order:
        // 1. a backdrop controller is attached: root and bar stay transparent so the blur comes through, the graphs
        //    area keeps its semi-transparent lift so the hierarchy survives on glass
        // 2. material "None" (Solid): the root takes the users own pick from settings, accent or custom and theme
        //    independent, the graphs area keeps the same lift on top of it
        // 3. otherwise (Mica/Acrylic while Windows transparency is off): every surface is its own flat opaque color
        //    and the graphs area carries no overlay at all
        //
        // case 3 is deliberately alpha free: bar and content used to be coupled through that overlay, so correcting
        // the base moved both at once and no measurement could be attributed to a single surface
        // all colors come out of the App.xaml theme dictionary, so every hex value lives in exactly one place; the
        // {ThemeResource} markup in the XAML is the first paint only, every later value is a local assignment from
        // here and a local value permanently outranks the markup expression
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

            // the window stroke is one opaque line in every mode
            // the separator is not: on glass the native line stays translucent and darkens the material rather
            // than covering it, so an opaque stroke there stands still while everything around it moves
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

        // === acrylic grain ===

        // the acrylic recipe composites a noise layer as its final step at 2 percent opacity, but the system
        // backdrop controller does not draw it, which is why the flyout reads as one perfectly flat tone while
        // the native shell surfaces scatter across a few levels
        // sc_noiseOpacity 0.02f and sc_blurRadius 30.0f are the published recipe constants:
        // https://github.com/microsoft/microsoft-ui-xaml/blob/6aed8d97fdecfe9b19d70c36bd1dacd9c6add7c1/dev/Materials/Acrylic/AcrylicBrush.h
        // this fills that layer in: one random grayscale bitmap, painted under every surface fill
        private const int NoiseSeed = 0x5EED;

        // layer opacity stays the recipe constant; how strong the grain reads is set through the value range
        // instead, which keeps the mean at 128 so tuning the grain never moves the calibrated surface colors
        private const double NoiseLayerOpacity = 0.02;
        private const double NoiseSpreadLevels = 3.5;

        private Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap? _noiseBitmap;
        private double _noiseScale;

        // grows on demand and never shrinks, so only a resize past the current bitmap rebuilds it
        // sized in physical pixels and scaled back down, so one noise pixel lands on one physical pixel instead
        // of being smeared across the DPI scale factor, which is what makes the grain look coarse
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

            // Stretch None keeps one bitmap pixel on one physical pixel, any stretching smears the grain away
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

        // applies the backdrop material for the current setting and the Windows transparency state
        //
        // "Mica" deliberately runs through DesktopAcrylicController as well, with the MicaPreset constants at the top
        // of this file instead of the settings sliders: real Mica only samples the wallpaper and shows next to nothing
        // on a small flyout sitting above the taskbar, while acrylic blurs what is actually behind the window
        // WidgetWindow uses a real MicaController for the same setting name, so the two windows differ on purpose
        // the tint and luminosity sliders from settings only reach the "Acrylic" branch of UpdateAcrylicProperties
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
                // Base is the acrylic variant the Windows 11 shell surfaces use; Default lets the system pick
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

            // single entry point for the base color, it reads the controller fields set just above
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
        // problem: when Windows transparency/theme changes, WinUI 3 DesktopAcrylicController needs a backdrop re-bind
        // to attach its blur shader to the newly created DWM swapchain
        // fix: after window recreation, briefly kick the backdrop pipeline (None -> Mica/Acrylic) to force DWM compositor refresh
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
