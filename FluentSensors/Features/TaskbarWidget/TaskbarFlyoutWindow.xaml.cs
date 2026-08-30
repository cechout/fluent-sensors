using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
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
    // 2. supports asymmetric top-and-right resizing via InputNonClientPointerSource while locking bottom-left anchors in WM_SIZING (0x0214)
    // 3. places the window directly beneath Shell_TrayWnd in Z-order so it slides out from under the taskbar
    // 4. coordinates a physical window slide via DispatcherTimer with direct composition opacity fading
    // 5. applies dynamic DWM corner preferences and shadow suppression depending on the Windows transparency setting
    // 6. integrates DesktopAcrylicController / Mica system backdrop with a swapchain kick on theme changes
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
        public const int WindowSlideDistanceDip = 280;

        // animation duration in milliseconds
        public const int EnterAnimationDurationMs = 240; // duration on open (move-in)
        public const int ExitAnimationDurationMs = 140;  // duration on close (move-out)

        // fade opacity (1.0f = no fade, 0.0f = full fade)
        public const float EnterFadeStartOpacity = 0.9f; // initial opacity when opening
        public const float EnterFadeEndOpacity = 1.0f; // target opacity when opening
        public const float ExitFadeEndOpacity = 0.9f; // target opacity when closing

        // background live graph rendering toggle
        // keeps graphs rendering continuously in background so they are instantly visible on open
        public const bool KeepFlyoutGraphsActiveInBackground = true;

        // padding settings for fine-tuning
        public static readonly Thickness FlyoutRootPadding = new Thickness(0, 0, 0, 0); // padding applied to flyout root border
        public static readonly Thickness FlyoutGraphsPadding = new Thickness(5, 1, 5, 6);

        // --- Mica Flyout Blur Preset Settings (used when BackdropType == "Mica" and Transparency is ON) ---
        // Dark Mode Preset (#292929, Luminosity 0.90, Tint 0.70):
        public static readonly Windows.UI.Color MicaPresetDarkTintColor = Windows.UI.Color.FromArgb(255, 0x29, 0x29, 0x29);
        public const float MicaPresetDarkLuminosity = 0.90f;
        public const float MicaPresetDarkTintOpacity = 0.70f;

        // Light Mode Preset (#F2F2F2, Luminosity 0.90, Tint 0.60):
        public static readonly Windows.UI.Color MicaPresetLightTintColor = Windows.UI.Color.FromArgb(255, 0xF2, 0xF2, 0xF2);
        public const float MicaPresetLightLuminosity = 0.90f;
        public const float MicaPresetLightTintOpacity = 0.60f;


        // === fields ===

        private const string WindowKey = "TaskbarFlyout";

        // Anchor offsets configurable in code-behind
        public const int FlyoutMarginToTaskbarDip = 12; // vertical gap between taskbar top and flyout bottom edge
        public const int FlyoutHorizontalOffsetDip = 2; // horizontal offset relative to taskbar widget left (negative = left, positive = right)

        // default and minimum width in DIP (matching standard WidgetWindow width)
        public const int FlyoutDefaultWidthDip = 250;
        public const int MinFlyoutWidthFloorDip = 250;

        // default and minimum height per graph slot in DIP
        public const int FlyoutDefaultGraphHeightDip = 100;
        public const int MinFlyoutGraphHeightDip = 90;
        public const int FlyoutHeaderHeightDip = 31;
        public const int FlyoutGraphSpacingDip = 8;

        private AppWindow _appWindow;
        private IntPtr _hwnd;
        private WindowMessageMonitor _messageMonitor;
        private UISettings? _uiSettings;

        private int _bottomAnchorY;
        private int _leftAnchorX;
        private int _targetX;
        private int _targetY;
        private bool _isAdjustingPosition;
        private bool _isHiding;

        // native window animation timer
        private DispatcherTimer? _animTimer;
        private Stopwatch? _animStopwatch;
        private int _animStartX, _animStartY, _animTargetX, _animTargetY;
        private Action? _animOnComplete;

        public TaskbarWidgetViewModel ViewModel { get; }
        public static TaskbarFlyoutWindow? CurrentInstance { get; private set; }
        private static TaskbarFlyoutWindow? _retainedInstance;

        // system backdrop controllers
        private DesktopAcrylicController? _acrylicController;
        private MicaController? _micaController;
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
            presenter.IsResizable = false; // resize is handled via InputNonClientPointerSource, keeping WS_THICKFRAME off
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
            // 2. WM_SIZING (0x0214): locks the Bottom-Left anchor coordinates during resizing
            // 3. WM_EXITSIZEMOVE (0x0232): saves window size when resize ends
            // 4. WM_SYSCOMMAND (0x0112): prevents dragging/moving the window
            _messageMonitor = new WindowMessageMonitor(_hwnd);
            _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;

            // theming & backdrop (Taskbar ecosystem)
            SetBackdrop(SettingsService.Instance.TaskbarBackdropType);
            ApplyTheme(SettingsService.Instance.AppTheme);

            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            SettingsService.Instance.TaskbarBackdropTypeChanged += OnBackdropTypeChanged;
            SettingsService.Instance.TaskbarOpacityChanged += OnOpacityChanged;
            SettingsService.Instance.TaskbarTintColorChanged += OnTintColorChanged;

            ((FrameworkElement)this.Content).ActualThemeChanged += (s, e) =>
            {
                this.DispatcherQueue.TryEnqueue(UpdateSolidBackground);
            };

            // initialize shadow policy based on Windows transparency setting
            InitializeShadowPolicy();

            // apply configurable paddings
            RootGrid.Padding = FlyoutRootPadding;
            GraphsContentGrid.Padding = FlyoutGraphsPadding;

            _appWindow.Changed += AppWindow_Changed;
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
                _uiSettings.AdvancedEffectsEnabledChanged += (s, e) => ScheduleRecreation();
                _uiSettings.ColorValuesChanged += (s, e) => ScheduleRecreation();
                UpdateShadowPolicy();
            }
            catch
            {
                // safety guard if UISettings is unavailable
            }
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


        // === non-client resize regions (Top and Right only) ===

        private void UpdateNonClientResizeRegions()
        {
            try
            {
                var nonClientInput = InputNonClientPointerSource.GetForWindowId(_appWindow.Id);
                if (nonClientInput == null) return;

                int width = _appWindow.Size.Width;
                int height = _appWindow.Size.Height;
                int border = (int)Math.Round(10 * GetScaleFactor());

                // Top border extending full width (including top-right corner)
                var topRect = new RectInt32(0, 0, width, border);
                // Right border extending full height (including top-right corner)
                var rightRect = new RectInt32(Math.Max(0, width - border), 0, border, height);

                nonClientInput.SetRegionRects(NonClientRegionKind.TopBorder, new[] { topRect });
                nonClientInput.SetRegionRects(NonClientRegionKind.RightBorder, new[] { rightRect });

                nonClientInput.ClearRegionRects(NonClientRegionKind.LeftBorder);
                nonClientInput.ClearRegionRects(NonClientRegionKind.BottomBorder);
            }
            catch
            {
                // safety guard if platform version does not support InputNonClientPointerSource
            }
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
            else if (e.Message.MessageId == 0x0214) // WM_SIZING
            {
                var rect = Marshal.PtrToStructure<NativeMethods.RECT>(e.Message.LParam);
                double scale = GetScaleFactor();

                // lock bottom and left anchors
                if (_bottomAnchorY > 0)
                {
                    rect.Bottom = _bottomAnchorY;
                    rect.Left = _leftAnchorX;
                }

                // enforce minimum width and height constraints
                int minW = (int)Math.Round(MinFlyoutWidthFloorDip * scale);
                int minH = CalculateFlyoutMinHeight(ViewModel.PinnedSensors.Count, scale);

                if (rect.Right - rect.Left < minW)
                {
                    rect.Right = rect.Left + minW;
                }
                if (rect.Bottom - rect.Top < minH)
                {
                    rect.Top = rect.Bottom - minH;
                }

                Marshal.StructureToPtr(rect, e.Message.LParam, false);
                e.Result = (IntPtr)1; // TRUE
                e.Handled = true;
            }
            else if (e.Message.MessageId == 0x0232) // WM_EXITSIZEMOVE
            {
                UpdateNonClientResizeRegions();
                UpdateShadowPolicy();
                SaveWindowState();
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

        // resets saved flyout dimensions so the size is cleanly recalculated on next open / button click
        public static void ResetGeometry()
        {
            var state = WindowStateService.Instance.GetState(WindowKey);
            if (state != null)
            {
                state.Width = 0;
                state.Height = 0;
                WindowStateService.Instance.SetState(WindowKey, state);
            }

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

                window.PositionAboveTaskbar(widgetWindow, startForSlideAnimation: true);
                window.SetGraphsRenderingActive(true);
                window._appWindow.Show();
                window.EnsureBehindTaskbarZOrder();
                window.Activate();
                window.SlideInFromBottom();
                return;
            }

            var newWindow = new TaskbarFlyoutWindow(widgetWindow.ViewModel);
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
            }
            catch { }

            try
            {
                _messageMonitor?.Dispose();
                _messageMonitor = null;
                _acrylicController?.Dispose();
                _acrylicController = null;
                _micaController?.Dispose();
                _micaController = null;
                this.Close();
            }
            catch { }
        }

        private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _recreateDebounceTimer;

        // --- workaround: window recreation and backdrop toggle on global OS theme/transparency change ---
        // problem: the exact underlying reason why Windows DWM fails to bind DesktopAcrylicController blur properly
        // without a complete window recreation and a brief material toggle (None -> Mica) is not fully clear and is
        // based purely on empirical observation
        // fix: upon receiving a global theme or transparency change from Windows, both windows (TaskbarFlyoutWindow
        // and WidgetWindow, if open) are fully destroyed and rebuilt, and if Mica was selected, the backdrop material
        // setting is briefly toggled to None (Solid) and immediately back to Mica to force a full DWM compositor refresh
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

        private static void ExecuteFullRebuild()
        {
            var widgetWindow = TaskbarWidgetWindow.CurrentInstance;
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

            // 3. Rebuild TaskbarFlyoutWindow
            if (widgetWindow != null)
            {
                widgetWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    if (flyoutWasVisible)
                    {
                        ShowFlyout(widgetWindow);
                    }
                    else
                    {
                        Preload(widgetWindow);
                    }

                    // 4. Force SettingsService backdrop material toggle (Solid -> Mica)
                    var kickTimer = widgetWindow.DispatcherQueue.CreateTimer();
                    kickTimer.Interval = TimeSpan.FromMilliseconds(80);
                    kickTimer.IsRepeating = false;
                    kickTimer.Tick += (s, e) =>
                    {
                        kickTimer.Stop();
                        if (SettingsService.Instance.TaskbarBackdropType == "Mica")
                        {
                            SettingsService.Instance.TaskbarBackdropType = "None";
                            SettingsService.Instance.TaskbarBackdropType = "Mica";
                        }
                        if (SettingsService.Instance.BackdropType == "Mica")
                        {
                            SettingsService.Instance.BackdropType = "None";
                            SettingsService.Instance.BackdropType = "Mica";
                        }
                    };
                    kickTimer.Start();
                });
            }
        }


        // === physical window slide & content fade animations ===

        private void SlideInFromBottom()
        {
            TaskbarWidgetWindow.CurrentInstance?.SetFlyoutActive(true);

            // 1. Content Fade (DirectComposition)
            PlayContentFade(EnterFadeStartOpacity, EnterFadeEndOpacity, EnterAnimationDurationMs);

            // 2. Physical Window Slide Up
            int slideDistPx = (int)Math.Round(WindowSlideDistanceDip * GetScaleFactor());
            int startY = _targetY + slideDistPx;

            EnsureBehindTaskbarZOrder();
            AnimateNativeWindowPosition(_targetX, startY, _targetX, _targetY, EnterAnimationDurationMs, isEntering: true);
        }

        private void SlideOutToBottom(Action onCompleted)
        {
            TaskbarWidgetWindow.CurrentInstance?.SetFlyoutActive(false);

            // 1. Content Fade (DirectComposition)
            PlayContentFade(1.0f, ExitFadeEndOpacity, ExitAnimationDurationMs);

            // 2. Physical Window Slide Down
            int slideDistPx = (int)Math.Round(WindowSlideDistanceDip * GetScaleFactor());
            int currentX = _appWindow.Position.X;
            int currentY = _appWindow.Position.Y;
            int endY = _targetY + slideDistPx;

            EnsureBehindTaskbarZOrder();
            AnimateNativeWindowPosition(currentX, currentY, currentX, endY, ExitAnimationDurationMs, isEntering: false, onComplete: onCompleted);
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

        // calculates and applies the bottom-left anchored placement directly above the taskbar widget
        private void PositionAboveTaskbar(TaskbarWidgetWindow widgetWindow, bool startForSlideAnimation = false)
        {
            var widgetHwnd = WinRT.Interop.WindowNative.GetWindowHandle(widgetWindow);
            NativeMethods.GetWindowRect(widgetHwnd, out var widgetRect);

            var primaryTaskbar = WinTaskbarService.Instance.DiscoverNow().FirstOrDefault();
            double scale = primaryTaskbar != null ? (primaryTaskbar.Dpi / 96.0) : GetScaleFactor();

            var savedState = WindowStateService.Instance.GetState(WindowKey);

            // width is fixed to 310 DIP (matching standard WidgetWindow width)
            int defaultWidthPx = (int)Math.Round(FlyoutDefaultWidthDip * scale);
            int minWidthPx = defaultWidthPx;

            int sensorCount = ViewModel.PinnedSensors.Count;

            // enforce resize constraints: min-width is 310 DIP, min-height is the compact sensor height
            int minHeightDip = (int)Math.Round(CalculateFlyoutMinHeight(sensorCount, scale) / scale);
            var manager = WindowManager.Get(this);
            manager.MinWidth = MinFlyoutWidthFloorDip;
            manager.MinHeight = minHeightDip;

            int minHeightPx = (int)Math.Round(minHeightDip * scale);
            int defaultHeightPx = CalculateFlyoutDefaultHeight(sensorCount, scale);

            int desiredWidthPx;
            int desiredHeightPx;

            if (savedState != null && savedState.Width > 0 && savedState.Height > 0)
            {
                desiredWidthPx = Math.Max(savedState.Width, minWidthPx);
                desiredHeightPx = Math.Max(savedState.Height, minHeightPx);
            }
            else
            {
                // reset to exact default geometry
                desiredWidthPx = defaultWidthPx;
                desiredHeightPx = defaultHeightPx;
            }

            // calculate bottom-left anchor
            int marginPx = (int)Math.Round(FlyoutMarginToTaskbarDip * scale);
            int hOffsetPx = (int)Math.Round(FlyoutHorizontalOffsetDip * scale);

            _bottomAnchorY = primaryTaskbar != null ? (primaryTaskbar.Rect.Y - marginPx) : (widgetRect.Top - marginPx);
            _leftAnchorX = widgetRect.Left + hOffsetPx;

            _targetX = _leftAnchorX;
            _targetY = _bottomAnchorY - desiredHeightPx;

            // clamp within primary display work area
            var workArea = DisplayArea.Primary.WorkArea;
            if (_targetX + desiredWidthPx > workArea.X + workArea.Width - 10)
            {
                _targetX = workArea.X + workArea.Width - desiredWidthPx - 10;
            }
            if (_targetX < workArea.X + 10)
            {
                _targetX = workArea.X + 10;
            }
            if (_targetY < workArea.Y + 10)
            {
                _targetY = workArea.Y + 10;
            }

            int initialY = startForSlideAnimation
                ? (_targetY + (int)Math.Round(WindowSlideDistanceDip * scale))
                : _targetY;

            _isAdjustingPosition = true;
            _appWindow.MoveAndResize(new RectInt32(_targetX, initialY, desiredWidthPx, desiredHeightPx));
            _isAdjustingPosition = false;

            UpdateNonClientResizeRegions();
            UpdateShadowPolicy();
        }

        private double GetScaleFactor()
        {
            uint dpi = GetDpiForWindow(_hwnd);
            return dpi / 96.0;
        }

        private int CalculateFlyoutMinHeight(int sensorCount, double scaleFactor)
        {
            double minXamlHeight = FlyoutHeaderHeightDip + (sensorCount * (MinFlyoutGraphHeightDip + FlyoutGraphSpacingDip));
            return (int)(minXamlHeight * scaleFactor);
        }

        private int CalculateFlyoutDefaultHeight(int sensorCount, double scaleFactor)
        {
            double defaultXamlHeight = FlyoutHeaderHeightDip + (sensorCount * (FlyoutDefaultGraphHeightDip + FlyoutGraphSpacingDip));
            return (int)(defaultXamlHeight * scaleFactor);
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (_isAdjustingPosition || _animTimer?.IsEnabled == true) return;

            if (args.DidSizeChange && _bottomAnchorY > 0)
            {
                UpdateNonClientResizeRegions();
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
            state.Width = _appWindow.Size.Width;
            state.Height = _appWindow.Size.Height;
            state.SensorCount = ViewModel.PinnedSensors.Count;
            state.WasOpen = false;

            WindowStateService.Instance.SetState(WindowKey, state);
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

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            args.Cancel = true;
            HideFlyout();
            CurrentInstance = null;
            _retainedInstance = this;
        }


        // === theme and backdrop application (Taskbar Ecosystem) ===

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

        private void UpdateAcrylicProperties()
        {
            if (_isClosed) return;

            if (_acrylicController != null)
            {
                bool isLight = this.Content is FrameworkElement fe && fe.ActualTheme == ElementTheme.Light;
                string backdropType = SettingsService.Instance.TaskbarBackdropType;

                if (backdropType == "Mica")
                {
                    // Use dedicated custom Mica blur preset
                    if (isLight)
                    {
                        _acrylicController.TintColor = MicaPresetLightTintColor;
                        _acrylicController.TintOpacity = MicaPresetLightTintOpacity;
                        _acrylicController.LuminosityOpacity = MicaPresetLightLuminosity;
                        _acrylicController.FallbackColor = MicaPresetLightTintColor;
                    }
                    else
                    {
                        _acrylicController.TintColor = MicaPresetDarkTintColor;
                        _acrylicController.TintOpacity = MicaPresetDarkTintOpacity;
                        _acrylicController.LuminosityOpacity = MicaPresetDarkLuminosity;
                        _acrylicController.FallbackColor = MicaPresetDarkTintColor;
                    }
                }
                else
                {
                    // "Acrylic" uses user-configured settings sliders
                    Windows.UI.Color defaultTint = isLight
                        ? Windows.UI.Color.FromArgb(255, 0xF9, 0xF9, 0xF9)
                        : Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20);

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

        private void UpdateSolidBackground()
        {
            if (_isClosed || FlyoutRootBorder == null) return;

            bool isLight = this.Content is FrameworkElement fe && fe.ActualTheme == ElementTheme.Light;
            string backdropType = SettingsService.Instance.TaskbarBackdropType;

            if (backdropType == "None")
            {
                Windows.UI.Color targetColor = SettingsService.Instance.TaskbarUseAccentColor
                    ? (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]
                    : SettingsService.Instance.TaskbarCustomTintColor;

                FlyoutRootBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(targetColor);
            }
            else if (_acrylicController == null && _micaController == null)
            {
                // Native Windows 11 flyout background: #F9F9F9 for Light, #202020 for Dark
                Windows.UI.Color bgColor = isLight
                    ? Windows.UI.Color.FromArgb(255, 0xF9, 0xF9, 0xF9)
                    : Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20);

                FlyoutRootBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(bgColor);
            }
            else
            {
                // Backdrop controller (Acrylic/Mica) handles the translucent background
                FlyoutRootBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }

            // Native Windows 11 flyout border: #BFBFBF for Light, #383838 for Dark
            Windows.UI.Color borderColor = isLight
                ? Windows.UI.Color.FromArgb(255, 0xBF, 0xBF, 0xBF)
                : Windows.UI.Color.FromArgb(255, 0x38, 0x38, 0x38);

            FlyoutRootBorder.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(borderColor);
        }

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
            _micaController?.Dispose();
            _micaController = null;
            this.SystemBackdrop = null;

            if (isTransparencyEnabled && (backdropType == "Acrylic" || backdropType == "Mica") && DesktopAcrylicController.IsSupported())
            {
                _acrylicController = new DesktopAcrylicController();
                _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
                _acrylicController.SetSystemBackdropConfiguration(_configurationSource);

                UpdateAcrylicProperties();
                FlyoutRootBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else
            {
                this.SystemBackdrop = new TransparentTintBackdrop();
                UpdateSolidBackground();
            }

            UpdateShadowPolicy();
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
