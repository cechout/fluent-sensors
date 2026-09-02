using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics;
using WinUIEx;
using WinUIEx.Messaging;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Controls.SensorRow;
using FluentSensors.Core.Taskbar;
using FluentSensors.Features.Sensors;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.TaskbarWidget
{
    // the taskbar widget window, embedded as a direct child of Shell_TrayWnd rather than floating above it
    //
    // as a direct child of the taskbar (WS_CHILD via SetParent), there is no z-order contest with other windows
    // and the widget belongs to the taskbar directly without flickering
    // clicking the widget button toggles the companion TaskbarFlyoutWindow positioned above it
    //
    // references:
    // https://devblogs.microsoft.com/oldnewthing/20130605-00/?p=4183 (cross-process child window embedding)
    // https://github.com/zhongyang219/TrafficMonitor (taskbar telemetry embedding reference)
    // https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mouseactivate
    public sealed partial class TaskbarWidgetWindow : Window
    {
        // === win32 api imports ===

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);


        // === fields ===

        // logical pixels, scaled to the taskbars DPI before use, so these read the same at any scaling
        public const double VerticalMarginTopDip = 2.5; // gap above widget in DIP (1 mm = 3.78 DIP)
        public const double VerticalMarginBottomDip = 2.0; // gap below widget in DIP (1 mm = 3.78 DIP)
        private const int AnchorOffsetDip = 10; // gap between the widget and the anchored end of the taskbar
        private const int TaskbarHorizontalPaddingDip = 10; // minimum margin to the outer left/right edges of the taskbar
        private const int SensorSlotWidthDip = 120; // width per pinned sensor slot
        private const int SensorSlotSpacingDip = 8; // spacing between sensor slots
        private const int ButtonPaddingDip = 0; // inner horizontal padding of the taskbar button
        private const int MinimumWidgetWidthDip = 60; // fallback width when no sensors are pinned

        // maybe a user setting?
        private const TaskbarAnchor Anchor = TaskbarAnchor.Start;

        // --- taskbar startup animation settings ---
        public const int TaskbarStartupSlideDistanceDip = 40; // startup slide distance in DIP/pixels (e.g. 30 to 60)
        public const int TaskbarStartupDurationMs = 260; // startup animation duration in milliseconds
        public const int TaskbarStartupDelayMs = 1200; // startup animation delay in milliseconds (delays animation until window creation/charts finish)
        public const float TaskbarStartupStartOpacity = 0.0f; // startup fade opacity (0.0f = full fade in, 1.0f = no fade)

        // --- drag-to-reposition settings ---
        private const string WindowKey = "TaskbarWidget";
        private const int DragThresholdPixels = 4; // minimum movement in physical pixels before entering drag mode
        private bool _isPotentialDrag;
        private bool _isDragging;
        private bool _suppressClick;
        private int _dragStartCursorScreenX;
        private int _dragStartWindowScreenX;
        private RectInt32 _dragTaskbarRect;
        private uint _dragTaskbarDpi;
        private RectInt32 _currentScreenRect;
        private int _currentOffsetDip = AnchorOffsetDip;

        // --- taskbar button animation timings (in milliseconds) ---
        private const int HoverBackgroundDelayMs = 0; // delay before hover background starts (Standard Windows: 0ms)
        private const int HoverBackgroundDurationMs = 120; // duration of hover background fade-in (Standard Windows: 83ms [ControlFastAnimationDuration])
        private const int HoverStrokeDurationMs = 0; // duration of border stroke appearance on hover (Standard Windows: 0ms instant)
        private const int ExitBackgroundDurationMs = 180; // duration of background fade-out on exit (Standard Windows: 167ms [ControlNormalAnimationDuration])
        private const int ExitStrokeDurationMs = 40; // duration of border stroke fade-out on exit (Standard Windows: 40ms [ControlFasterAnimationDuration])
        private const int PressDurationMs = 50; // duration of press feedback animation (Standard Windows: 50ms)

        // embedding can fail transiently, e.g. while the start menu is open or another app is mid-embed
        // retrying up to 5 times avoids reporting a false failure
        private const int MaxEmbedAttempts = 5;
        private static readonly TimeSpan EmbedRetryDelay = TimeSpan.FromMilliseconds(500);
        private int _embedAttempt;

        private AppWindow _appWindow;
        private IntPtr _hwnd;
        private IntPtr _taskbarHwnd; // parent we are embedded into, zero while detached
        private WindowMessageMonitor _nonActivatingMonitor; // see WinNonActivatingWindow.Apply; must stay alive in field
        private bool _isEmbedded;
        private bool _embedGaveUp;
        public bool IsEmbedded => _isEmbedded;

        // set only on the window a rebuild creates to replace a live one: it carries its predecessors drag offset,
        // skips the startup animation, and reopens the flyout if the rebuild interrupted an open one
        private bool _isRebuild;
        private bool _restoreFlyoutAfterEmbed;

        private static TaskbarWidgetWindow _retainedInstance;
        public static TaskbarWidgetWindow CurrentInstance { get; private set; }
        public static event Action WidgetStateChanged;

        public TaskbarWidgetViewModel ViewModel { get; }


        // === constructor ===

        public TaskbarWidgetWindow() : this(ResolveSensors(SensorSelectionService.Instance.GetSelection(SensorSelectionProfile.Taskbar)))
        {
        }

        public TaskbarWidgetWindow(List<SensorRowViewModel> selectedSensors)
        {
            ViewModel = new TaskbarWidgetViewModel(selectedSensors);
            Initialize();
        }

        // rebuild path: takes over the ViewModel of the window it replaces, so the pinned graphs keep their history
        // and the old instance leaves no second HardwareDataUpdated subscription behind
        // see TaskbarFlyoutWindow.ScheduleRecreation for why the window is rebuilt at all
        private TaskbarWidgetWindow(TaskbarWidgetViewModel viewModel, int offsetDip, bool restoreFlyout)
        {
            ViewModel = viewModel;
            _currentOffsetDip = offsetDip;
            _isRebuild = true;
            _restoreFlyoutAfterEmbed = restoreFlyout;
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                this.InitializeComponent();
                CurrentInstance = this;

                _appWindow = this.AppWindow;
                _appWindow.IsShownInSwitchers = false;
                _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

                // restore previously saved drag offset along the taskbar if available
                // a rebuild already carries the offset of the window it replaces, see the rebuild constructor
                if (!_isRebuild)
                {
                    var savedState = WindowStateService.Instance.GetState(WindowKey);
                    if (savedState != null && savedState.X > 0)
                    {
                        _currentOffsetDip = savedState.X;
                    }
                }

                // --- workaround: CreateForContextMenu crashes unpackaged ---
                // problem: OverlappedPresenter.CreateForContextMenu() throws a TargetInvocationException
                // in unpackaged apps (WindowsPackageType=None, which FluentSensors uses); confirmed
                // Microsoft-internal repro matches our exact csproj setup:
                // https://github.com/microsoft/microsoft-ui-xaml/issues/6765
                // fix: build the same visual result by hand via OverlappedPresenter.Create()
                var presenter = OverlappedPresenter.Create();
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                // deliberately no IsAlwaysOnTop: an embedded child is ordered inside the taskbar
                _appWindow.SetPresenter(presenter);

                // the widget sits on the taskbar, so it has to let the bar show through instead of painting a rectangle of its own
                // set here, while the window is still a normal top level one: WinUIEx TransparentTintBackdrop
                // is built on DwmExtendFrameIntoClientArea and DwmEnableBlurBehindWindow, and DWM only manages top level windows
                this.SystemBackdrop = new TransparentTintBackdrop();

                // move offscreen before initial activation so no un-embedded frame or border flashes on the desktop
                _appWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));

                _appWindow.Closing += AppWindow_Closing;

                SettingsService.Instance.TaskbarGraphWidthChanged += OnTaskbarGraphWidthChanged;
                SettingsService.Instance.ThemeChanged += OnThemeChanged;
                ApplyTheme(SettingsService.Instance.AppTheme);

                // wire left-button press/release/drag animations and movement even when Button internally handles clicks
                TaskbarButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(TaskbarButton_PointerPressed), true);
                TaskbarButton.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(TaskbarButton_PointerReleased), true);
                TaskbarButton.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(TaskbarButton_PointerMoved), true);
                TaskbarButton.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(TaskbarButton_PointerCaptureLost), true);

                TaskbarButton.Loaded += (s, e) =>
                {
                    TaskbarButton.ApplyTemplate();
                    EnsureCompositionElements();
                };

                ((FrameworkElement)this.Content).ActualThemeChanged += (s, e) =>
                {
                    this.DispatcherQueue.TryEnqueue(UpdateVisualState);
                };

                // embedding is queued immediately and also guarded via Loaded and a watchdog timer
                // ensuring offscreen windows during cold startup never miss initialization
                ((FrameworkElement)this.Content).Loaded += (s, e) =>
                {
                    if (!_isEmbedded && !_embedGaveUp)
                    {
                        EmbedIntoTaskbar();
                    }
                };

                this.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_isEmbedded && !_embedGaveUp)
                    {
                        EmbedIntoTaskbar();
                    }
                });

                CurrentInstance = this;
                WidgetStateChanged?.Invoke();

                this.Activate();
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Fluent Sensors", $"Taskbar widget initialization failed:\n\n{ex.Message}");
            }
        }


        // === public methods ===

        // shows the widget with the given sensors, reusing the previously hidden window instance if one exists instead of
        // creating a new one every time (see _retainedInstance), same pattern as WidgetWindow
        public static void ShowWithSensors(List<SensorRowViewModel> selectedSensors)
        {
            if (CurrentInstance != null)
            {
                CurrentInstance.ReconfigureFor(selectedSensors);
                CurrentInstance.ViewModel.SetLiveDataActive(true);
                CurrentInstance.SetGraphsRenderingActive(true);

                // if the window was never successfully embedded (e.g. startup failed or previously gave up),
                // reset attempt counters and trigger embedding cleanly
                if (!CurrentInstance._isEmbedded || CurrentInstance._embedGaveUp)
                {
                    CurrentInstance._embedAttempt = 0;
                    CurrentInstance._embedGaveUp = false;
                    CurrentInstance._appWindow.Show(false);
                    CurrentInstance.EmbedIntoTaskbar();
                }
                else
                {
                    CurrentInstance.Activate();
                }

                WidgetStateChanged?.Invoke();
                return;
            }

            if (_retainedInstance != null)
            {
                var window = _retainedInstance;
                _retainedInstance = null;
                CurrentInstance = window;

                window._embedAttempt = 0;
                window._embedGaveUp = false;
                window.ReconfigureFor(selectedSensors);
                window.ViewModel.SetLiveDataActive(true);
                window.SetGraphsRenderingActive(true);

                // detached on hide, so this embeds again rather than just showing
                window._appWindow.Show(false);
                window.EmbedIntoTaskbar();
                WidgetStateChanged?.Invoke();
                return;
            }

            _ = new TaskbarWidgetWindow(selectedSensors);
        }

        // shows the widget using whatever sensors are currently saved under SensorSelectionProfile.Taskbar
        public static void ShowWidget()
        {
            var ids = SensorSelectionService.Instance.GetSelection(SensorSelectionProfile.Taskbar);
            var sensors = ResolveSensors(ids);
            ShowWithSensors(sensors);
        }

        // rebuilds the widgets content and updates the window width on the taskbar
        public void ReconfigureFor(List<SensorRowViewModel> selectedSensors)
        {
            ViewModel.Reconfigure(selectedSensors);

            if (_isEmbedded && _taskbarHwnd != IntPtr.Zero)
            {
                PositionOnTaskbar();
            }

            // reset flyout size on button click so it is cleanly recalculated
            TaskbarFlyoutWindow.ResetGeometry();
        }

        private bool _isClosed = false;

        // --- memory leak: taskbar widget instance never released after a real close ---
        // problem: WinUI 3 never releases secondary Window objects back to the GC/OS after a real close
        // confirmed, still-open platform bug, reproducible even with empty window content:
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // everywhere else the answer is hide-and-reuse (CloseWidget re-registers _retainedInstance); this method is
        // the one place that deliberately destroys the window, because a global transparency or accent change is only
        // picked up by a window built after it, see TaskbarFlyoutWindow.ScheduleRecreation
        // price: one leaked CCW per OS theme or transparency change, knowingly paid
        //
        // only ever call this from RecreateWindow, never from the normal close path
        public void SafeDestroy(bool disposeViewModel)
        {
            if (_isClosed) return;
            _isClosed = true;

            try
            {
                SettingsService.Instance.TaskbarGraphWidthChanged -= OnTaskbarGraphWidthChanged;
                SettingsService.Instance.ThemeChanged -= OnThemeChanged;
            }
            catch { }

            // AppWindow_Closing cancels the close and re-registers this instance as _retainedInstance; detaching it
            // here is what lets the Close below go through instead of resurrecting a window that is already torn down
            try
            {
                _appWindow.Closing -= AppWindow_Closing;
            }
            catch { }

            try
            {
                if (_taskbarHwnd != IntPtr.Zero)
                {
                    WinTaskbarEmbedder.Detach(_hwnd);
                    _taskbarHwnd = IntPtr.Zero;
                    _isEmbedded = false;
                }
            }
            catch { }

            try
            {
                // only the instance whose ViewModel nobody takes over releases it; the live one hands it to its
                // replacement, and cleaning it up here would drop the graph history and the sensor subscription
                if (disposeViewModel)
                {
                    ViewModel?.Cleanup();
                }

                _nonActivatingMonitor?.Dispose();
                _nonActivatingMonitor = null;
                this.Close();
            }
            catch { }
        }

        private static bool _isRecreating = false;

        // fully destroys and rebuilds the taskbar widget when Windows global transparency or accent color changes,
        // and tells the new window whether it has to bring an open flyout back with it
        public static void RecreateWindow(bool restoreFlyout)
        {
            if (_isRecreating) return;
            if (CurrentInstance == null && _retainedInstance == null) return;
            _isRecreating = true;

            try
            {
                var live = CurrentInstance;
                var carriedViewModel = live?.ViewModel;
                int carriedOffsetDip = live?._currentOffsetDip ?? AnchorOffsetDip;

                if (live != null)
                {
                    CurrentInstance = null;
                    live.SafeDestroy(disposeViewModel: false);
                }

                // a hidden widget has nothing on screen to refresh, so it is dropped rather than rebuilt; the next
                // ShowWidget then builds one that matches the new OS settings
                if (_retainedInstance != null)
                {
                    var old = _retainedInstance;
                    _retainedInstance = null;
                    old.SafeDestroy(disposeViewModel: true);
                }

                if (carriedViewModel == null) return;

                // one dispatcher hop, so the Close above drains before the replacement window is built
                var queue = DispatcherQueue.GetForCurrentThread() ?? MainWindow.CurrentInstance?.DispatcherQueue;
                if (queue == null || !queue.TryEnqueue(() => _ = new TaskbarWidgetWindow(carriedViewModel, carriedOffsetDip, restoreFlyout)))
                {
                    _ = new TaskbarWidgetWindow(carriedViewModel, carriedOffsetDip, restoreFlyout);
                }
            }
            finally
            {
                _isRecreating = false;
            }
        }


        // === embedding ===

        // finds the primary taskbar, calculates placement, and reparents into it via SetParent
        private void EmbedIntoTaskbar()
        {
            try
            {
                // Shell_TrayWnd is always found before any Shell_SecondaryTrayWnd, so the first entry is the primary bar
                var primaryTaskbar = WinTaskbarService.Instance.DiscoverNow().FirstOrDefault();
                if (primaryTaskbar == null)
                {
                    RetryOrReportFailure("No taskbar found");
                    return;
                }

                _taskbarHwnd = primaryTaskbar.Hwnd;

                int widthDip = CalculateWidgetWidthDip(ViewModel.PinnedSensors.Count);
                double scale = primaryTaskbar.Dpi / 96.0;
                int topMarginPx = (int)Math.Round(VerticalMarginTopDip * scale);

                int bottomMarginPx = (int)Math.Round(VerticalMarginBottomDip * scale);

                var screenRect = TaskbarWidgetPlacement.Calculate(
                    primaryTaskbar,
                    Anchor,
                    (int)(_currentOffsetDip * scale),
                    (int)(widthDip * scale),
                    topMarginPx,
                    bottomMarginPx);

                _currentScreenRect = screenRect;

                if (!WinTaskbarEmbedder.Embed(_hwnd, _taskbarHwnd, screenRect, out int errorCode))
                {
                    _taskbarHwnd = IntPtr.Zero;
                    RetryOrReportFailure($"SetParent failed with Win32 error {errorCode}");
                    return;
                }

                // suppresses focus stealing on click via WM_MOUSEACTIVATE returning MA_NOACTIVATE
                _nonActivatingMonitor = WinNonActivatingWindow.Apply(_hwnd);

                // Win32-level mouse tracking: ensures the very first hover triggers instantly without needing a prior click
                // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-trackmouseevent
                _nonActivatingMonitor.WindowMessageReceived += (s, e) =>
                {
                    const uint WM_SETCURSOR = 0x0020;
                    const uint WM_MOUSEMOVE = 0x0200;
                    const uint WM_MOUSELEAVE = 0x02A3;

                    if (e.Message.MessageId == WM_SETCURSOR || e.Message.MessageId == WM_MOUSEMOVE)
                    {
                        if (!_isPointerOver)
                        {
                            _isPointerOver = true;
                            var tme = new NativeMethods.TRACKMOUSEEVENT
                            {
                                cbSize = (uint)Marshal.SizeOf<NativeMethods.TRACKMOUSEEVENT>(),
                                dwFlags = NativeMethods.TME_LEAVE,
                                hwndTrack = _hwnd
                            };
                            NativeMethods.TrackMouseEvent(ref tme);
                            this.DispatcherQueue.TryEnqueue(() =>
                            {
                                TaskbarButton_PointerEntered(TaskbarButton, null);
                            });
                        }
                    }
                    else if (e.Message.MessageId == WM_MOUSELEAVE)
                    {
                        if (_isPointerOver)
                        {
                            _isPointerOver = false;
                            this.DispatcherQueue.TryEnqueue(() =>
                            {
                                TaskbarButton_PointerExited(TaskbarButton, null);
                            });
                        }
                    }
                };

                _embedAttempt = 0;
                _isEmbedded = true;
                SaveWindowState(wasOpen: true);

                // a rebuild replaces a widget that is already sitting on the taskbar, so it skips the startup
                // sequence; otherwise every OS accent or transparency change would blank the button for
                // TaskbarStartupDelayMs and slide it in again
                if (!_isRebuild)
                {
                    // hide TaskbarButton initially before the delayed startup animation begins
                    if (TaskbarButton != null)
                    {
                        try
                        {
                            TaskbarButton.ApplyTemplate();
                            var visual = ElementCompositionPreview.GetElementVisual(TaskbarButton);
                            if (visual != null)
                            {
                                float slideDistPx = (float)(TaskbarStartupSlideDistanceDip * scale);
                                visual.Offset = new Vector3(0, slideDistPx, 0);
                                visual.Opacity = TaskbarStartupStartOpacity;
                            }
                        }
                        catch { }
                    }

                    // play smooth slide-up startup animation on TaskbarButton after the specified startup delay
                    if (TaskbarStartupDelayMs > 0)
                    {
                        var animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TaskbarStartupDelayMs) };
                        animTimer.Tick += (s, e) =>
                        {
                            animTimer.Stop();
                            PlayStartupAnimation();
                        };
                        animTimer.Start();
                    }
                    else
                    {
                        this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, PlayStartupAnimation);
                    }
                }

                // preload flyout window into memory to eliminate first-open latency
                // a rebuild that interrupted an open flyout reopens it here instead: the flyout anchors to the widget
                // rect, so it can only be placed once the new widget sits on the taskbar again
                if (_restoreFlyoutAfterEmbed)
                {
                    _restoreFlyoutAfterEmbed = false;
                    TaskbarFlyoutWindow.ShowFlyout(this);
                }
                else
                {
                    TaskbarFlyoutWindow.Preload(this);
                }
            }
            catch (Exception ex)
            {
                RetryOrReportFailure(ex.Message);
            }
        }

        // repositions and resizes the already-embedded window according to the current sensor count
        private void PositionOnTaskbar()
        {
            var primaryTaskbar = WinTaskbarService.Instance.DiscoverNow().FirstOrDefault();
            if (primaryTaskbar == null) return;

            int widthDip = CalculateWidgetWidthDip(ViewModel.PinnedSensors.Count);
            double scale = primaryTaskbar.Dpi / 96.0;
            int topMarginPx = (int)Math.Round(VerticalMarginTopDip * scale);
            int bottomMarginPx = (int)Math.Round(VerticalMarginBottomDip * scale);

            var screenRect = TaskbarWidgetPlacement.Calculate(
                primaryTaskbar,
                Anchor,
                (int)(_currentOffsetDip * scale),
                (int)(widthDip * scale),
                topMarginPx,
                bottomMarginPx);

            _currentScreenRect = screenRect;
            WinTaskbarEmbedder.Position(_hwnd, _taskbarHwnd, screenRect);
        }

        // retries embedding a few times before giving up: a transient failure (start menu open, another app mid-embed,
        // or dormant Windows 11 Widgets shell host) usually clears up a moment later on its own
        private void RetryOrReportFailure(string reason)
        {
            _embedAttempt++;
            if (_embedAttempt < MaxEmbedAttempts)
            {
                // on the first failed attempt, wake the dormant Windows 11 Widgets shell host
                // to ensure Shell_TrayWnd initializes its XAML Island composition tree
                if (_embedAttempt == 1)
                {
                    _ = Task.Run(async () =>
                    {
                        await WinShellHelper.WakeWidgetsSubsystemAsync();
                    });
                }

                var retryTimer = DispatcherQueue.CreateTimer();
                retryTimer.Interval = EmbedRetryDelay;
                retryTimer.IsRepeating = false;
                retryTimer.Tick += (s, e) => EmbedIntoTaskbar();
                retryTimer.Start();
            }
            else
            {
                _embedGaveUp = true;
                WidgetStateChanged?.Invoke();
                ShowErrorMessage("Fluent Sensors", $"Taskbar widget embedding failed after {MaxEmbedAttempts} attempts:\n\n{reason}");
            }
        }

        // closes both the taskbar widget and flyout cleanly, detaching from the taskbar shell
        public void CloseWidget()
        {
            TaskbarFlyoutWindow.CurrentInstance?.HideFlyout();

            CurrentInstance = null;
            _retainedInstance = this;

            SetGraphsRenderingActive(false);
            ViewModel?.SetLiveDataActive(false);

            if (_taskbarHwnd != IntPtr.Zero)
            {
                WinTaskbarEmbedder.Detach(_hwnd);
                _taskbarHwnd = IntPtr.Zero;
                _isEmbedded = false;
            }

            SaveWindowState(wasOpen: false);
            _appWindow.Hide();
            WidgetStateChanged?.Invoke();
        }

        // writes the current taskbar offset and open state to the window state store
        private void SaveWindowState(bool wasOpen = true)
        {
            var state = WindowStateService.Instance.GetState(WindowKey) ?? new Persistence.Models.WindowState();
            state.X = _currentOffsetDip;
            state.WasOpen = wasOpen;
            WindowStateService.Instance.SetState(WindowKey, state);
        }

        // --- memory leak: TaskbarWidgetWindow never released after close ---
        // problem: WinUI 3 never releases secondary Window objects back to the GC/OS after a real close
        // confirmed, still-open platform bug, reproducible even with empty window content:
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // fix: hide instead of actually closing, and keep this instance around (_retainedInstance) for reuse
        // same approach as WidgetWindow
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // SafeDestroy is tearing this instance down: let the close proceed instead of handing a window that is
            // already torn down back to _retainedInstance, where the next ShowWithSensors would try to reuse it
            if (_isClosed) return;

            args.Cancel = true;
            CloseWidget();
        }

        private void OnThemeChanged(string newTheme)
        {
            this.DispatcherQueue.TryEnqueue(() => ApplyTheme(newTheme));
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
            UpdateVisualState();
        }

        private void OnTaskbarGraphWidthChanged(int newWidth)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (_isEmbedded && _taskbarHwnd != IntPtr.Zero)
                {
                    PositionOnTaskbar();
                }
            });
        }

        // maps sensor count to total DIP width, including button padding, slot widths, and slot gaps
        private static int CalculateWidgetWidthDip(int sensorCount)
        {
            if (sensorCount <= 0)
            {
                return MinimumWidgetWidthDip;
            }

            int slotWidth = SettingsService.Instance.TaskbarGraphWidthDip;
            int contentWidth = (sensorCount * slotWidth) + ((sensorCount - 1) * SensorSlotSpacingDip);
            return contentWidth + (ButtonPaddingDip * 2);
        }

        private static List<SensorRowViewModel> ResolveSensors(IReadOnlyList<string> sensorIds)
        {
            if (sensorIds == null || sensorIds.Count == 0 || SensorsViewModel.Instance == null)
            {
                return new List<SensorRowViewModel>();
            }

            var allSensors = SensorsViewModel.Instance.HardwareGroups
                .SelectMany(g => g.Sensors.Concat(g.HiddenSensors));

            return sensorIds
                .Select(id => allSensors.FirstOrDefault(s => s.Id == id))
                .Where(sensor => sensor != null)
                .ToList();
        }

        private void SetGraphsRenderingActive(bool active)
        {
            if (this.Content is DependencyObject root)
            {
                SensorGraphRenderingGate.SetActive(root, active);
            }
        }


        // === user interaction & directcomposition visual states ===

        private Visual _backgroundVisual;
        private Visual _pressedVisual;
        private Visual _activeHoverVisual;
        private Visual _activePressedVisual;
        private Visual _strokeVisual;
        private Visual _activeHoverStrokeVisual;
        private Visual _activePressedStrokeVisual;
        private Visual _contentVisual;
        private Compositor _compositor;

        private bool _isFlyoutActive;
        private bool _isPointerOver;
        private bool _isPressed;

        public void SetFlyoutActive(bool active)
        {
            if (_isFlyoutActive == active) return;
            _isFlyoutActive = active;
            this.DispatcherQueue.TryEnqueue(UpdateVisualState);
        }

        private void PlayStartupAnimation()
        {
            if (TaskbarButton == null) return;
            var visual = ElementCompositionPreview.GetElementVisual(TaskbarButton);
            var compositor = visual?.Compositor;
            if (compositor == null) return;

            var primaryTaskbar = WinTaskbarService.Instance.DiscoverNow().FirstOrDefault();
            double scale = primaryTaskbar != null ? (primaryTaskbar.Dpi / 96.0) : 1.0;
            float slideDistPx = (float)(TaskbarStartupSlideDistanceDip * scale);

            // Fluent 2 Decelerate Curve: cubic-bezier(0, 0, 0, 1)
            var easeOut = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.0f, 0.0f),
                new Vector2(0.0f, 1.0f));

            var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
            offsetAnim.InsertKeyFrame(0.0f, new Vector3(0, slideDistPx, 0));
            offsetAnim.InsertKeyFrame(1.0f, new Vector3(0, 0, 0), easeOut);
            offsetAnim.Duration = TimeSpan.FromMilliseconds(TaskbarStartupDurationMs);
            visual.StartAnimation("Offset", offsetAnim);

            if (TaskbarStartupStartOpacity < 1.0f)
            {
                var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
                opacityAnim.InsertKeyFrame(0.0f, TaskbarStartupStartOpacity);
                opacityAnim.InsertKeyFrame(1.0f, 1.0f, easeOut);
                opacityAnim.Duration = TimeSpan.FromMilliseconds(TaskbarStartupDurationMs);
                visual.StartAnimation("Opacity", opacityAnim);
            }
            else
            {
                visual.Opacity = 1.0f;
            }
        }

        private void EnsureCompositionElements()
        {
            if (_backgroundVisual != null) return;

            TaskbarButton.ApplyTemplate();

            var bgBorder = FindVisualChild<Border>(TaskbarButton, "BackgroundBorder");
            var pressedBorder = FindVisualChild<Border>(TaskbarButton, "PressedBorder");
            var activeHoverBorder = FindVisualChild<Border>(TaskbarButton, "ActiveHoverBorder");
            var activePressedBorder = FindVisualChild<Border>(TaskbarButton, "ActivePressedBorder");
            var strokeBorder = FindVisualChild<Border>(TaskbarButton, "StrokeBorder");
            var activeHoverStrokeBorder = FindVisualChild<Border>(TaskbarButton, "ActiveHoverStrokeBorder");
            var activePressedStrokeBorder = FindVisualChild<Border>(TaskbarButton, "ActivePressedStrokeBorder");
            var contentPresenter = FindVisualChild<ContentPresenter>(TaskbarButton, "ContentPresenter");

            if (bgBorder != null)
            {
                _backgroundVisual = ElementCompositionPreview.GetElementVisual(bgBorder);
                _compositor = _backgroundVisual?.Compositor;
            }
            if (pressedBorder != null)
            {
                _pressedVisual = ElementCompositionPreview.GetElementVisual(pressedBorder);
            }
            if (activeHoverBorder != null)
            {
                _activeHoverVisual = ElementCompositionPreview.GetElementVisual(activeHoverBorder);
            }
            if (activePressedBorder != null)
            {
                _activePressedVisual = ElementCompositionPreview.GetElementVisual(activePressedBorder);
            }
            if (strokeBorder != null)
            {
                _strokeVisual = ElementCompositionPreview.GetElementVisual(strokeBorder);
            }
            if (activeHoverStrokeBorder != null)
            {
                _activeHoverStrokeVisual = ElementCompositionPreview.GetElementVisual(activeHoverStrokeBorder);
            }
            if (activePressedStrokeBorder != null)
            {
                _activePressedStrokeVisual = ElementCompositionPreview.GetElementVisual(activePressedStrokeBorder);
            }
            if (contentPresenter != null)
            {
                _contentVisual = ElementCompositionPreview.GetElementVisual(contentPresenter);
            }
        }

        private void UpdateVisualState()
        {
            EnsureCompositionElements();
            if (_compositor == null) return;

            if (!_isFlyoutActive)
            {
                // === Flyout Closed ===
                AnimateVisualOpacity(_activeHoverStrokeVisual, 0.0f, ExitStrokeDurationMs);
                AnimateVisualOpacity(_activePressedStrokeVisual, 0.0f, ExitStrokeDurationMs);

                if (_isPressed)
                {
                    AnimateVisualOpacity(_backgroundVisual, 0.0f, PressDurationMs);
                    AnimateVisualOpacity(_activeHoverVisual, 0.0f, PressDurationMs);
                    AnimateVisualOpacity(_activePressedVisual, 0.0f, PressDurationMs);
                    AnimateVisualOpacity(_pressedVisual, 1.0f, PressDurationMs);
                    AnimateVisualOpacity(_strokeVisual, 1.0f, PressDurationMs);
                }
                else if (_isPointerOver)
                {
                    AnimateVisualOpacity(_backgroundVisual, 1.0f, HoverBackgroundDurationMs, HoverBackgroundDelayMs);
                    AnimateVisualOpacity(_activeHoverVisual, 0.0f, HoverBackgroundDurationMs);
                    AnimateVisualOpacity(_activePressedVisual, 0.0f, HoverBackgroundDurationMs);
                    AnimateVisualOpacity(_pressedVisual, 0.0f, HoverBackgroundDurationMs);
                    AnimateVisualOpacity(_strokeVisual, 1.0f, HoverStrokeDurationMs);
                }
                else
                {
                    AnimateVisualOpacity(_backgroundVisual, 0.0f, ExitBackgroundDurationMs);
                    AnimateVisualOpacity(_activeHoverVisual, 0.0f, ExitBackgroundDurationMs);
                    AnimateVisualOpacity(_activePressedVisual, 0.0f, ExitBackgroundDurationMs);
                    AnimateVisualOpacity(_pressedVisual, 0.0f, ExitBackgroundDurationMs);
                    AnimateVisualOpacity(_strokeVisual, 0.0f, ExitStrokeDurationMs);
                }
            }
            else
            {
                // === Flyout Open ===
                AnimateVisualOpacity(_strokeVisual, 0.0f, ExitStrokeDurationMs);

                if (_isPressed)
                {
                    AnimateVisualOpacity(_backgroundVisual, 0.0f, PressDurationMs);
                    AnimateVisualOpacity(_activeHoverVisual, 0.0f, PressDurationMs);
                    AnimateVisualOpacity(_activePressedVisual, 1.0f, PressDurationMs);
                    AnimateVisualOpacity(_pressedVisual, 0.0f, PressDurationMs);

                    AnimateVisualOpacity(_activeHoverStrokeVisual, 0.0f, PressDurationMs);
                    AnimateVisualOpacity(_activePressedStrokeVisual, 1.0f, PressDurationMs);
                }
                else if (_isPointerOver)
                {
                    AnimateVisualOpacity(_backgroundVisual, 0.0f, HoverBackgroundDurationMs, HoverBackgroundDelayMs);
                    AnimateVisualOpacity(_activeHoverVisual, 1.0f, HoverBackgroundDurationMs, HoverBackgroundDelayMs);
                    AnimateVisualOpacity(_activePressedVisual, 0.0f, HoverBackgroundDurationMs);
                    AnimateVisualOpacity(_pressedVisual, 0.0f, HoverBackgroundDurationMs);

                    AnimateVisualOpacity(_activeHoverStrokeVisual, 1.0f, HoverStrokeDurationMs);
                    AnimateVisualOpacity(_activePressedStrokeVisual, 0.0f, HoverStrokeDurationMs);
                }
                else
                {
                    // Active Rest: stays in visual state "hover"
                    AnimateVisualOpacity(_backgroundVisual, 1.0f, ExitBackgroundDurationMs);
                    AnimateVisualOpacity(_activeHoverVisual, 0.0f, ExitBackgroundDurationMs);
                    AnimateVisualOpacity(_activePressedVisual, 0.0f, ExitBackgroundDurationMs);
                    AnimateVisualOpacity(_pressedVisual, 0.0f, ExitBackgroundDurationMs);

                    AnimateVisualOpacity(_strokeVisual, 1.0f, ExitStrokeDurationMs);
                    AnimateVisualOpacity(_activeHoverStrokeVisual, 0.0f, ExitStrokeDurationMs);
                    AnimateVisualOpacity(_activePressedStrokeVisual, 0.0f, ExitStrokeDurationMs);
                }
            }

            if (_contentVisual != null)
            {
                if (_isPressed)
                {
                    bool isLight = ((FrameworkElement)this.Content).ActualTheme == ElementTheme.Light;
                    float targetOpacity = isLight ? 0.70f : 0.95f;
                    AnimateVisualOpacity(_contentVisual, targetOpacity, PressDurationMs);
                }
                else
                {
                    AnimateVisualOpacity(_contentVisual, 1.0f, PressDurationMs);
                }
            }
        }

        private void AnimateVisualOpacity(Visual visual, float targetOpacity, int durationMs, int delayMs = 0)
        {
            if (visual == null || _compositor == null) return;

            if (durationMs <= 0)
            {
                visual.Opacity = targetOpacity;
                return;
            }

            var anim = _compositor.CreateScalarKeyFrameAnimation();
            anim.InsertKeyFrame(1.0f, targetOpacity);
            anim.Duration = TimeSpan.FromMilliseconds(durationMs);
            if (delayMs > 0)
            {
                anim.DelayTime = TimeSpan.FromMilliseconds(delayMs);
            }
            visual.StartAnimation("Opacity", anim);
        }

        private static T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == name)
                {
                    return element;
                }
                T result = FindVisualChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private void TaskbarButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            EnsureCompositionElements();
            if (_compositor == null)
            {
                _isPointerOver = false;
                return;
            }

            _isPointerOver = true;
            UpdateVisualState();
        }

        private void TaskbarButton_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging) return;

            _isPointerOver = false;
            _isPressed = false;
            UpdateVisualState();

            if (_contentVisual != null)
            {
                _contentVisual.Opacity = 1.0f;
            }
        }

        private void TaskbarButton_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var ptr = e?.GetCurrentPoint(TaskbarButton);
            if (ptr != null && !ptr.Properties.IsLeftButtonPressed) return;

            // skip all drag bookkeeping while the position is locked; press feedback and click-to-toggle stay live
            if (!SettingsService.Instance.TaskbarWidgetPositionLocked && NativeMethods.GetCursorPos(out var cursorPos))
            {
                _dragStartCursorScreenX = cursorPos.X;
                _dragStartWindowScreenX = _currentScreenRect.X;
                _isPotentialDrag = true;
                _isDragging = false;
                _suppressClick = false;

                var primaryTaskbar = WinTaskbarService.Instance.DiscoverNow().FirstOrDefault();
                if (primaryTaskbar != null)
                {
                    _dragTaskbarRect = primaryTaskbar.Rect;
                    _dragTaskbarDpi = primaryTaskbar.Dpi;
                }
            }

            _isPressed = true;
            UpdateVisualState();

            // content press feedback: 70% opacity in Light mode, 95% opacity in Dark mode
            if (_contentVisual != null && _compositor != null)
            {
                bool isLight = ((FrameworkElement)this.Content).ActualTheme == ElementTheme.Light;
                float targetOpacity = isLight ? 0.70f : 0.95f;
                var pressAnim = _compositor.CreateScalarKeyFrameAnimation();
                pressAnim.InsertKeyFrame(1.0f, targetOpacity);
                pressAnim.Duration = TimeSpan.FromMilliseconds(PressDurationMs);
                _contentVisual.StartAnimation("Opacity", pressAnim);
            }
        }

        private void TaskbarButton_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPotentialDrag || !_isEmbedded || _taskbarHwnd == IntPtr.Zero) return;

            if (!NativeMethods.GetCursorPos(out var currentCursorPos)) return;

            int deltaX = currentCursorPos.X - _dragStartCursorScreenX;

            if (!_isDragging && Math.Abs(deltaX) >= DragThresholdPixels)
            {
                _isDragging = true;
                if (e != null)
                {
                    TaskbarButton.CapturePointer(e.Pointer);
                }
            }

            if (_isDragging && _dragTaskbarRect.Width > 0)
            {
                double scale = (_dragTaskbarDpi > 0 ? _dragTaskbarDpi : 96.0) / 96.0;
                int paddingPx = (int)Math.Round(TaskbarHorizontalPaddingDip * scale);

                int minX = _dragTaskbarRect.X + paddingPx;
                int maxX = _dragTaskbarRect.X + _dragTaskbarRect.Width - _currentScreenRect.Width - paddingPx;
                if (maxX < minX) maxX = minX;

                int targetScreenX = Math.Clamp(_dragStartWindowScreenX + deltaX, minX, maxX);

                if (targetScreenX != _currentScreenRect.X)
                {
                    _currentScreenRect = new RectInt32(targetScreenX, _currentScreenRect.Y, _currentScreenRect.Width, _currentScreenRect.Height);
                    WinTaskbarEmbedder.Position(_hwnd, _taskbarHwnd, _currentScreenRect);

                    _currentOffsetDip = (int)Math.Round((targetScreenX - _dragTaskbarRect.X) / scale);
                }
            }
        }

        private void TaskbarButton_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                if (e != null)
                {
                    try { TaskbarButton.ReleasePointerCapture(e.Pointer); } catch { }
                }
                _isDragging = false;
                _isPotentialDrag = false;
                _suppressClick = true;
                SaveWindowState(wasOpen: true);
            }
            else
            {
                _isPotentialDrag = false;
            }

            _isPressed = false;

            // determine if pointer is still over the button in its new position
            bool isOverNow = false;
            if (NativeMethods.GetCursorPos(out var pt))
            {
                isOverNow = (pt.X >= _currentScreenRect.X && pt.X <= _currentScreenRect.X + _currentScreenRect.Width &&
                             pt.Y >= _currentScreenRect.Y && pt.Y <= _currentScreenRect.Y + _currentScreenRect.Height);
            }
            _isPointerOver = isOverNow;
            UpdateVisualState();

            if (_contentVisual != null && _compositor != null)
            {
                var relAnim = _compositor.CreateScalarKeyFrameAnimation();
                relAnim.InsertKeyFrame(1.0f, 1.0f);
                relAnim.Duration = TimeSpan.FromMilliseconds(PressDurationMs);
                _contentVisual.StartAnimation("Opacity", relAnim);
            }
        }

        private void TaskbarButton_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = false;
            _isPotentialDrag = false;
            _suppressClick = false;
            TaskbarButton_PointerExited(sender, e);
        }

        private void TaskbarButton_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = false;
            _isPotentialDrag = false;
            _suppressClick = false;
            TaskbarButton_PointerExited(sender, e);
        }

        private void TaskbarButton_Click(object sender, RoutedEventArgs e)
        {
            if (_suppressClick)
            {
                _suppressClick = false;
                return;
            }

            TaskbarFlyoutWindow.Toggle(this);
        }

        private void ShowErrorMessage(string title, string message)
        {
            MessageBoxW(_hwnd, message, title, 0x00000010); // MB_OK | MB_ICONERROR
        }
    }
}
