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
using WinRT;
using WinUIEx;
using WinUIEx.Messaging;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Controls.SensorRow;
using FluentSensors.Core.Taskbar;
using FluentSensors.Persistence.Models;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.TaskbarWidget
{
    public sealed partial class TaskbarFlyoutWindow : Window
    {
        // === win32 api imports ===

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, uint cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out NativeMethods.RECT pvAttribute, int cbAttribute);

        private const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        private enum DWM_WINDOW_CORNER_PREFERENCE
        {
            DWMWCP_DEFAULT = 0,
            DWMWCP_DONOTROUND = 1,
            DWMWCP_ROUND = 2, // 8px standard
            DWMWCP_ROUNDSMALL = 3 // 4px
        }


        // === animation settings ===

        // physical window slide distance in DIP/pixels
        public const int WindowSlideDistanceDip = 280;

        // animation duration in milliseconds
        public const int EnterAnimationDurationMs = 280; // duration on open (move-in)
        public const int ExitAnimationDurationMs = 180;  // duration on close (move-out)

        // fade opacity (1.0f = no fade, 0.0f = full fade)
        public const float EnterFadeStartOpacity = 0.9f; // initial opacity when opening
        public const float EnterFadeEndOpacity = 1.0f;   // target opacity when opening
        public const float ExitFadeEndOpacity = 0.9f;    // target opacity when closing


        // === fields ===

        private const string WindowKey = "TaskbarFlyout";

        // Anchor offsets configurable in code-behind
        private const int FlyoutMarginToTaskbarDip = 8; // vertical gap between taskbar top and flyout bottom edge
        private const int FlyoutHorizontalOffsetDip = 0; // horizontal offset relative to taskbar widget left

        private const int SingleSensorFlyoutWidthDip = 240; // clean compact width when only 1 sensor is pinned
        private const int MinFlyoutWidthFloorDip = 200; // absolute minimum width floor

        private AppWindow _appWindow;
        private IntPtr _hwnd;
        private WindowMessageMonitor _messageMonitor;

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

            // frameless resizable presenter without default OS caption buttons (no top-right close/min/max)
            var presenter = OverlappedPresenter.Create();
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = false; // do not force topmost above taskbar shell
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = true;
            _appWindow.SetPresenter(presenter);

            // apply Windows 11 rounded window corners (DWMWA_WINDOW_CORNER_PREFERENCE = 33)
            int cornerPreference = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

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

            _appWindow.Changed += AppWindow_Changed;
            _appWindow.Closing += AppWindow_Closing;
            this.Activated += Window_Activated;
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
            });
        }


        // === physical window slide & content fade animations ===

        private void SlideInFromBottom()
        {
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

            int sensorCount = ViewModel.PinnedSensors.Count;
            int actualWidgetPhysicalWidth = widgetRect.Right - widgetRect.Left;

            // check if sensor count changed since last saved geometry
            var savedState = WindowStateService.Instance.GetState(WindowKey);
            bool sensorCountChanged = (savedState == null || savedState.SensorCount != sensorCount);

            // width determination:
            // 1 sensor: 240 DIP
            // 2+ sensors: matches taskbar widget exact physical width
            int defaultWidthPx = sensorCount <= 1
                ? (int)Math.Round(SingleSensorFlyoutWidthDip * scale)
                : actualWidgetPhysicalWidth;

            int minWidthDip = sensorCount <= 1
                ? MinFlyoutWidthFloorDip
                : Math.Min(SingleSensorFlyoutWidthDip, (int)Math.Round(actualWidgetPhysicalWidth / scale));

            // enforce resize constraints
            ApplyMinimumWindowSize(sensorCount, minWidthDip, scale);

            int minWidthPx = (int)Math.Round(minWidthDip * scale);
            int minHeightPx = CalculateFlyoutMinHeight(sensorCount, scale);
            int defaultHeightPx = CalculateFlyoutDefaultHeight(sensorCount, scale);

            int desiredWidthPx;
            int desiredHeightPx;

            if (!sensorCountChanged && savedState != null && savedState.Width > 0 && savedState.Height > 0)
            {
                desiredWidthPx = Math.Max(savedState.Width, minWidthPx);
                desiredHeightPx = Math.Max(savedState.Height, minHeightPx);
            }
            else
            {
                // reset to exact default geometry for new sensor selection
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
        }

        private double GetScaleFactor()
        {
            uint dpi = GetDpiForWindow(_hwnd);
            return dpi / 96.0;
        }

        private int CalculateFlyoutDefaultHeight(int sensorCount, double scaleFactor)
        {
            double desiredXamlHeight = 31 + (sensorCount * (104 + 8));
            int physicalHeight = (int)(desiredXamlHeight * scaleFactor);
            int screenHeight = DisplayArea.Primary.WorkArea.Height;
            return Math.Min(physicalHeight, screenHeight - 60);
        }

        private int CalculateFlyoutMinHeight(int sensorCount, double scaleFactor)
        {
            double minXamlHeight = 31 + (sensorCount * (90 + 8));
            return (int)(minXamlHeight * scaleFactor);
        }

        private void ApplyMinimumWindowSize(int sensorCount, int minWidthDip, double scaleFactor)
        {
            var manager = WindowManager.Get(this);
            manager.MinWidth = minWidthDip;
            manager.MinHeight = (int)Math.Round(CalculateFlyoutMinHeight(sensorCount, scaleFactor) / scaleFactor);
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (_isAdjustingPosition || _animTimer?.IsEnabled == true) return;

            if (args.DidSizeChange && _bottomAnchorY > 0)
            {
                UpdateNonClientResizeRegions();
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
            if (_acrylicController != null)
            {
                Windows.UI.Color targetColor = SettingsService.Instance.TaskbarUseAccentColor
                    ? (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]
                    : SettingsService.Instance.TaskbarCustomTintColor;

                _acrylicController.TintColor = targetColor;
                _acrylicController.TintOpacity = SettingsService.Instance.TaskbarTintOpacity;
                _acrylicController.LuminosityOpacity = SettingsService.Instance.TaskbarLuminosityOpacity;
            }
        }

        private void UpdateSolidBackground()
        {
            if (SettingsService.Instance.TaskbarBackdropType == "None")
            {
                Windows.UI.Color targetColor = SettingsService.Instance.TaskbarUseAccentColor
                    ? (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]
                    : SettingsService.Instance.TaskbarCustomTintColor;

                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(targetColor);
            }
        }

        public void SetBackdrop(string backdropType)
        {
            DispatcherQueue.EnsureSystemDispatcherQueue();

            if (_configurationSource == null)
            {
                _configurationSource = new SystemBackdropConfiguration();
                this.Activated += (s, e) => { if (_configurationSource != null) _configurationSource.IsInputActive = true; };
                ((FrameworkElement)this.Content).ActualThemeChanged += (s, e) => SetConfigurationSourceTheme();

                _configurationSource.IsInputActive = true;
                SetConfigurationSourceTheme();
            }

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
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else if (backdropType == "Mica" && MicaController.IsSupported())
            {
                _micaController = new MicaController();
                _micaController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
                _micaController.SetSystemBackdropConfiguration(_configurationSource);

                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else
            {
                UpdateSolidBackground();
            }
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
    }
}
