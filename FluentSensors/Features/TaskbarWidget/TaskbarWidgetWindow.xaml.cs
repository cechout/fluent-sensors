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

using FluentSensors.Common.Localization;
using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Controls.SensorRow;
using FluentSensors.Core.Taskbar;
using FluentSensors.Features.Sensors;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.TaskbarWidget
{
    // the taskbar widget:
    // a child of Shell_TrayWnd (WS_CHILD via SetParent) rather than a window floating above it, so there is no z-order
    // contest and no flicker; its button toggles the TaskbarFlyoutWindow next to it
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

        // in DIP, scaled to the taskbar DPI; inner faces the desktop, outer the screen edge (above and below the
        // widget on a bottom taskbar)
        public const double InnerMarginDip = 2.5;
        public const double OuterMarginDip = 2.0;
        // the small taskbar sets its own buttons closer to its edges (WinTaskbarInfo.IsCompact)
        public const double CompactInnerMarginDip = 0.8;
        public const double CompactOuterMarginDip = 0.2;
        private const int AnchorOffsetDip = 10; // to the anchored end of the taskbar
        private const int TaskbarEndPaddingDip = 10; // minimum to both ends of the taskbar
        private const int SensorSlotWidthDip = 120; // unused; the slot width is the graph width setting
        private const int SensorSlotSpacingDip = 8;
        private const int ButtonPaddingDip = 0; // along the taskbar, in the length math
        private const int MinimumWidgetLengthDip = 60; // with no sensors pinned

        // button padding in the frame of the graph, not of the taskbar edge; (the graph is asymmetric by itself,
        // a 3 DIP label gap above it and the chart 2 DIP below its card); calibrated on the bottom
        // taskbar, see ApplyButtonPadding
        private const double ButtonPaddingEndsDip = 4; // both ends of the slot row
        private const double ButtonPaddingGraphTopDip = 0.5;
        private const double ButtonPaddingGraphBottomDip = 2.5; // the graph baseline side
        private const double CompactButtonPaddingGraphTopDip = -0.3;
        private const double CompactButtonPaddingGraphBottomDip = 1.7;

        // fixed; (could become a user setting)
        private const TaskbarAnchor Anchor = TaskbarAnchor.Start;

        // --- taskbar startup animation settings ---
        public const int TaskbarStartupSlideDistanceDip = 40;
        public const int TaskbarStartupDurationMs = 260;
        public const int TaskbarStartupDelayMs = 1200; // until window and charts are ready
        public const float TaskbarStartupStartOpacity = 0.0f;

        // --- drag-to-reposition settings ---
        private const string WindowKey = "TaskbarWidget";
        private const int DragThresholdPixels = 4; // physical px before a drag starts
        private bool _isPotentialDrag;
        private bool _isDragging;
        private bool _suppressClick;
        private int _dragStartCursorAlong; // along the taskbar, at press
        private int _dragStartWindowAlong;
        private WinTaskbarInfo? _dragTaskbar;
        private RectInt32 _currentScreenRect;

        // drag offsets from the taskbar start, one per screen edge, so the widget is where it was left
        // when the taskbar comes back
        private readonly Dictionary<ScreenEdge, int> _offsetsDip = new();

        // taskbar snapshot the widget was last laid out for; FollowTaskbar compares against it to skip polls that
        // only changed a secondary taskbar
        private WinTaskbarInfo? _placedTaskbar;

        private ScreenEdge PlacedEdge => _placedTaskbar?.Edge ?? ScreenEdge.Bottom;

        private int CurrentOffsetDip
        {
            get => _offsetsDip.TryGetValue(PlacedEdge, out int offsetDip) ? offsetDip : AnchorOffsetDip;
            set => _offsetsDip[PlacedEdge] = value;
        }

        // --- taskbar button animation settings ---
        // in ms; (each one matches the Windows taskbar button)
        private const int HoverBackgroundDelayMs = 0;
        private const int HoverBackgroundDurationMs = 83; // ControlFasterAnimationDuration
        private const int HoverStrokeDurationMs = 0;
        private const int ExitBackgroundDurationMs = 167; // ControlFastAnimationDuration
        private const int ExitStrokeDurationMs = 40;
        private const int PressDurationMs = 50;
        private const float PressContentOpacity = 0.75f; // content dim while held; both themes

        // embedding can fail transiently (start menu open, another app mid-embed), so it is retried before reporting
        private const int MaxEmbedAttempts = 5;
        private static readonly TimeSpan EmbedRetryDelay = TimeSpan.FromMilliseconds(500);
        private int _embedAttempt;

        private AppWindow _appWindow;
        private IntPtr _hwnd;
        private IntPtr _taskbarHwnd; // the parent; zero while detached
        private WindowMessageMonitor _nonActivatingMonitor; // see WinNonActivatingWindow.Apply; must stay alive in field
        private bool _isEmbedded;
        private bool _embedGaveUp;
        public bool IsEmbedded => _isEmbedded;

        // only on a window a rebuild creates: carries the drag offsets, skips the startup animation,
        // reopens an interrupted flyout
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

        // rebuild path: takes over the ViewModel of the window it replaces, so the graphs keep their history and no
        // second HardwareDataUpdated subscription is left; (why it rebuilds: TaskbarFlyoutWindow.ScheduleRecreation)
        private TaskbarWidgetWindow(TaskbarWidgetViewModel viewModel, Dictionary<ScreenEdge, int> offsetsDip, bool restoreFlyout)
        {
            ViewModel = viewModel;
            foreach (var pair in offsetsDip)
            {
                _offsetsDip[pair.Key] = pair.Value;
            }
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

                // saved drag offsets; (a rebuild carries its predecessors)
                if (!_isRebuild)
                {
                    LoadOffsets();
                }

                // --- workaround: CreateForContextMenu crashes unpackaged ---
                // problem: OverlappedPresenter.CreateForContextMenu() throws a TargetInvocationException in unpackaged
                // apps (WindowsPackageType=None, the GitHub builds); a confirmed Microsoft repro matches this csproj:
                // https://github.com/microsoft/microsoft-ui-xaml/issues/6765
                // fix: the same result by hand via OverlappedPresenter.Create()
                var presenter = OverlappedPresenter.Create();
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                // deliberately no IsAlwaysOnTop: an embedded child is ordered inside the taskbar
                _appWindow.SetPresenter(presenter);

                // transparent, so the taskbar shows through; set while still a top level window (WinUIEx
                // TransparentTintBackdrop works through DWM, which only manages top level windows)
                this.SystemBackdrop = new TransparentTintBackdrop();

                // off screen before the first activation, so no frame flashes on the desktop
                _appWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));

                _appWindow.Closing += AppWindow_Closing;

                SettingsService.Instance.TaskbarGraphWidthChanged += OnTaskbarGraphWidthChanged;
                SettingsService.Instance.TaskbarSideGraphDirectionChanged += OnTaskbarSideGraphDirectionChanged;
                SettingsService.Instance.TaskbarSideTitleLinesChanged += OnTaskbarSideTitleLinesChanged;
                ApplyWindowsTheme();

                // with handledEventsToo, the Button handles the pointer events itself
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

                // embedding is queued right away and again on Loaded, so a cold start never misses it
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

                // before the open state goes out, so the flyout shortcut registers with the first widget
                FlyoutShortcutRegistration.EnsureInitialized();

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

        // shows the widget, reusing the hidden _retainedInstance if there is one (same pattern as WidgetWindow)
        public static void ShowWithSensors(List<SensorRowViewModel> selectedSensors)
        {
            if (CurrentInstance != null)
            {
                CurrentInstance.ReconfigureFor(selectedSensors);
                CurrentInstance.ViewModel.SetLiveDataActive(true);
                CurrentInstance.SetGraphsRenderingActive(true);

                // never embedded or gave up: reset the attempts and embed again
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

        // with the sensors saved under the taskbar profile
        public static void ShowWidget()
        {
            var ids = SensorSelectionService.Instance.GetSelection(SensorSelectionProfile.Taskbar);
            var sensors = ResolveSensors(ids);
            ShowWithSensors(sensors);
        }

        // rebuilds the content and resizes the widget on the taskbar
        public void ReconfigureFor(List<SensorRowViewModel> selectedSensors)
        {
            ViewModel.Reconfigure(selectedSensors);

            if (_isEmbedded && _taskbarHwnd != IntPtr.Zero)
            {
                PositionOnTaskbar();
            }

            // the flyout follows the new sensor count
            TaskbarFlyoutWindow.ResetGeometry();
        }

        private bool _isClosed = false;

        // --- memory leak: taskbar widget instance never released after a real close ---
        // problem: WinUI 3 never releases a closed secondary Window (confirmed, still open, even with empty content):
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // fix: none here; the one place that destroys for real, since an OS theme or transparency change only reaches a
        // window built after it (see TaskbarFlyoutWindow.ScheduleRecreation); one leaked CCW per change, knowingly paid
        // only ever called from RecreateWindow
        public void SafeDestroy(bool disposeViewModel)
        {
            if (_isClosed) return;
            _isClosed = true;

            try
            {
                SettingsService.Instance.TaskbarGraphWidthChanged -= OnTaskbarGraphWidthChanged;
                SettingsService.Instance.TaskbarSideGraphDirectionChanged -= OnTaskbarSideGraphDirectionChanged;
                SettingsService.Instance.TaskbarSideTitleLinesChanged -= OnTaskbarSideTitleLinesChanged;
                StopTrackingTaskbar();
            }
            catch { }

            // detached first, or AppWindow_Closing cancels the Close below and brings this
            // window back as _retainedInstance
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
                // only an instance nobody takes the ViewModel from releases it; (the live one
                // hands it to its replacement)
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

        // destroys and rebuilds the widget, on an OS theme or transparency change and when explorer.exe rebuilt the
        // taskbar; tells the new window whether to bring an open flyout back
        public static void RecreateWindow(bool restoreFlyout)
        {
            if (_isRecreating) return;
            if (CurrentInstance == null && _retainedInstance == null) return;
            _isRecreating = true;

            try
            {
                var live = CurrentInstance;
                var carriedViewModel = live?.ViewModel;
                var carriedOffsetsDip = new Dictionary<ScreenEdge, int>(live?._offsetsDip ?? new Dictionary<ScreenEdge, int>());

                // the carried ViewModel is not rebuilt, so its graph colors are refreshed for
                // an accent change mid-rebuild
                carriedViewModel?.RefreshGraphColors();

                if (live != null)
                {
                    CurrentInstance = null;
                    live.SafeDestroy(disposeViewModel: false);
                }

                // a hidden widget is dropped, not rebuilt; the next ShowWidget builds a fresh one
                if (_retainedInstance != null)
                {
                    var old = _retainedInstance;
                    _retainedInstance = null;
                    old.SafeDestroy(disposeViewModel: true);
                }

                if (carriedViewModel == null) return;

                // one dispatcher hop, so the Close above drains before the replacement window is built
                var queue = DispatcherQueue.GetForCurrentThread() ?? MainWindow.CurrentInstance?.DispatcherQueue;
                if (queue == null || !queue.TryEnqueue(() => _ = new TaskbarWidgetWindow(carriedViewModel, carriedOffsetsDip, restoreFlyout)))
                {
                    _ = new TaskbarWidgetWindow(carriedViewModel, carriedOffsetsDip, restoreFlyout);
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

                ApplyEdgeLayout(primaryTaskbar);
                var screenRect = CalculateScreenRect(primaryTaskbar);

                _currentScreenRect = screenRect;

                if (!WinTaskbarEmbedder.Embed(_hwnd, _taskbarHwnd, screenRect, out int errorCode))
                {
                    _taskbarHwnd = IntPtr.Zero;
                    RetryOrReportFailure($"SetParent failed with Win32 error {errorCode}");
                    return;
                }

                // suppresses focus stealing on click via WM_MOUSEACTIVATE returning MA_NOACTIVATE
                _nonActivatingMonitor = WinNonActivatingWindow.Apply(_hwnd);

                // win32 mouse tracking, so the first hover registers without a prior click:
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
                StartTrackingTaskbar();

                // a rebuild skips the startup sequence; (otherwise every rebuild would blank the
                // button for TaskbarStartupDelayMs)
                if (!_isRebuild)
                {
                    // hidden until the delayed startup animation
                    if (TaskbarButton != null)
                    {
                        try
                        {
                            TaskbarButton.ApplyTemplate();
                            var visual = ElementCompositionPreview.GetElementVisual(TaskbarButton);
                            if (visual != null)
                            {
                                visual.Offset = GetStartupSlideOffset(primaryTaskbar);
                                visual.Opacity = TaskbarStartupStartOpacity;
                            }
                        }
                        catch { }
                    }

                    // the startup animation, after TaskbarStartupDelayMs
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

                // preloads the flyout; a rebuild that interrupted an open flyout reopens it here instead, once the
                // new widget sits on the taskbar
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

        // repositions the embedded widget for the current sensor count and taskbar edge
        private void PositionOnTaskbar()
        {
            var primaryTaskbar = WinTaskbarService.Instance.DiscoverNow().FirstOrDefault();
            if (primaryTaskbar == null) return;

            ApplyEdgeLayout(primaryTaskbar);
            var screenRect = CalculateScreenRect(primaryTaskbar);

            _currentScreenRect = screenRect;
            WinTaskbarEmbedder.Position(_hwnd, _taskbarHwnd, screenRect);
        }

        // the widget rect on the given taskbar: length from the pinned sensor count, position from the drag offset
        // of the matching orientation, kept inside the taskbar the same way a drag is
        private RectInt32 CalculateScreenRect(WinTaskbarInfo taskbar)
        {
            double scale = taskbar.Dpi / 96.0;
            int lengthPx = (int)(CalculateWidgetLengthDip(ViewModel.PinnedSensors.Count) * scale);
            int innerMarginPx = (int)Math.Round((taskbar.IsCompact ? CompactInnerMarginDip : InnerMarginDip) * scale);
            int outerMarginPx = (int)Math.Round((taskbar.IsCompact ? CompactOuterMarginDip : OuterMarginDip) * scale);

            return TaskbarWidgetPlacement.Calculate(
                taskbar,
                Anchor,
                ClampOffsetPx(taskbar, (int)(CurrentOffsetDip * scale), lengthPx),
                lengthPx,
                innerMarginPx,
                outerMarginPx);
        }

        // keeps an offset along the taskbar at least TaskbarEndPaddingDip away from both of its ends; the start end
        // wins when the widget is too long to honor both
        private static int ClampOffsetPx(WinTaskbarInfo taskbar, int offsetPx, int lengthPx)
        {
            double scale = taskbar.Dpi / 96.0;
            int paddingPx = (int)Math.Round(TaskbarEndPaddingDip * scale);
            int barLength = taskbar.IsVertical ? taskbar.Rect.Height : taskbar.Rect.Width;

            int minOffset = paddingPx;
            int maxOffset = barLength - lengthPx - paddingPx;
            if (maxOffset < minOffset) maxOffset = minOffset;

            return Math.Clamp(offsetPx, minOffset, maxOffset);
        }

        // turns the slot row and button padding to the taskbar edge; panel and template only swap on an orientation
        // change (a swap rebuilds every graph), existing side slots just get the new edge
        private void ApplyEdgeLayout(WinTaskbarInfo taskbar)
        {
            _placedTaskbar = taskbar;

            // graph width, time range, flyout alignment and the side slot layout are kept per edge and follow it here
            SettingsService.Instance.ActiveTaskbarEdge = taskbar.Edge.ToString();

            var slotsPanel = (ItemsPanelTemplate)RootGrid.Resources[taskbar.IsVertical ? "VerticalSlotsPanel" : "HorizontalSlotsPanel"];
            if (SlotsItemsControl.ItemsPanel != slotsPanel)
            {
                SlotsItemsControl.ItemsPanel = slotsPanel;
            }

            var slotTemplate = (DataTemplate)RootGrid.Resources[taskbar.IsVertical ? "SideSlotTemplate" : "HorizontalSlotTemplate"];
            if (SlotsItemsControl.ItemTemplate != slotTemplate)
            {
                SlotsItemsControl.ItemTemplate = slotTemplate;
            }

            RefreshSideSlots();
            ApplyButtonPadding(taskbar);
        }

        // follows the graph, not the edge (unlike the margins in CalculateScreenRect):
        // bottom and top - the graph stands upright, the bottom calibration unmirrored
        // left and right - centered across the taskbar, the larger padding on both sides
        private void ApplyButtonPadding(WinTaskbarInfo taskbar)
        {
            double graphTop = taskbar.IsCompact ? CompactButtonPaddingGraphTopDip : ButtonPaddingGraphTopDip;
            double graphBottom = taskbar.IsCompact ? CompactButtonPaddingGraphBottomDip : ButtonPaddingGraphBottomDip;

            double across = Math.Max(graphTop, graphBottom);
            TaskbarButton.Padding = taskbar.IsVertical
                ? new Thickness(across, ButtonPaddingEndsDip, across, ButtonPaddingEndsDip)
                : new Thickness(ButtonPaddingEndsDip, graphTop, ButtonPaddingEndsDip, graphBottom);
        }

        // the startup slide comes in from the screen edge the taskbar is docked to
        private static Vector3 GetStartupSlideOffset(WinTaskbarInfo taskbar)
        {
            float distance = (float)(TaskbarStartupSlideDistanceDip * (taskbar.Dpi / 96.0));

            return taskbar.Edge switch
            {
                ScreenEdge.Top => new Vector3(0, -distance, 0),
                ScreenEdge.Left => new Vector3(-distance, 0, 0),
                ScreenEdge.Right => new Vector3(distance, 0, 0),
                _ => new Vector3(0, distance, 0)
            };
        }

        // retries a few times before giving up; a transient failure (start menu open, another app mid-embed, a dormant
        // Widgets host) usually clears a moment later
        private void RetryOrReportFailure(string reason)
        {
            _embedAttempt++;
            if (_embedAttempt < MaxEmbedAttempts)
            {
                // the first failure wakes the dormant Widgets host, so Shell_TrayWnd builds its XAML Island tree
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
                ShowErrorMessage("Fluent Sensors", AppStrings.Format("TaskbarWidget_EmbedFailed", MaxEmbedAttempts, reason));
            }
        }

        // hides widget and flyout and detaches from the taskbar; (the instance is retained)
        public void CloseWidget()
        {
            TaskbarFlyoutWindow.CurrentInstance?.HideFlyout();

            CurrentInstance = null;
            _retainedInstance = this;

            SetGraphsRenderingActive(false);
            ViewModel?.SetLiveDataActive(false);
            StopTrackingTaskbar();

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

        // the open state goes to the shared window state, every drag offset to the state of its edge (WindowKey +
        // edge), where X carries the offset
        private void SaveWindowState(bool wasOpen = true)
        {
            var state = WindowStateService.Instance.GetState(WindowKey) ?? new Persistence.Models.WindowState();
            state.WasOpen = wasOpen;
            WindowStateService.Instance.SetState(WindowKey, state);

            foreach (var pair in _offsetsDip)
            {
                var edgeState = WindowStateService.Instance.GetState(WindowKey + pair.Key) ?? new Persistence.Models.WindowState();
                edgeState.X = pair.Value;
                WindowStateService.Instance.SetState(WindowKey + pair.Key, edgeState);
            }
        }

        // the counterpart of SaveWindowState; an edge without its own state takes the legacy shared offset, a
        // horizontal one, so only bottom and top inherit it
        private void LoadOffsets()
        {
            var shared = WindowStateService.Instance.GetState(WindowKey);

            foreach (var edge in Enum.GetValues<ScreenEdge>())
            {
                var edgeState = WindowStateService.Instance.GetState(WindowKey + edge);
                if (edgeState != null && edgeState.X >= 0)
                {
                    _offsetsDip[edge] = edgeState.X;
                }
                else if (shared != null && shared.X >= 0 && edge is ScreenEdge.Bottom or ScreenEdge.Top)
                {
                    _offsetsDip[edge] = shared.X;
                }
            }
        }

        // --- memory leak: TaskbarWidgetWindow never released after close ---
        // problem: WinUI 3 never releases a closed secondary Window (confirmed, still open, even with empty content):
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // fix: hide instead of closing and keep the instance as _retainedInstance (same as WidgetWindow)
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // SafeDestroy is closing for real; a closed window must not land in _retainedInstance
            // for ShowWithSensors to reuse
            if (_isClosed) return;

            args.Cancel = true;
            CloseWidget();
        }

        // sits inside the Windows taskbar, so it follows the Windows theme and ignores the app theme setting (every
        // other window follows the setting)
        // ElementTheme.Default inherits the app level theme, which is the Windows one, and keeps
        // tracking it, so this is set once
        private void ApplyWindowsTheme()
        {
            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = ElementTheme.Default;
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

        // length along the taskbar for n sensors; (on a vertical taskbar the graph width setting is the slot height)
        private static int CalculateWidgetLengthDip(int sensorCount)
        {
            if (sensorCount <= 0)
            {
                return MinimumWidgetLengthDip;
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

            // walks the groups, not the id list, so the order matches a live pin; (the saved list is in toggle order)
            var wantedIds = new HashSet<string>(sensorIds);

            return SensorsViewModel.Instance.HardwareGroups
                .SelectMany(g => g.Sensors.Concat(g.HiddenSensors))
                .Where(sensor => wantedIds.Contains(sensor.Id))
                .ToList();
        }

        private void SetGraphsRenderingActive(bool active)
        {
            if (this.Content is DependencyObject root)
            {
                SensorGraphRenderingGate.SetActive(root, active);
            }
        }


        // === side taskbar slots ===

        // a side slot gets its layout as soon as it exists; later changes reach it through RefreshSideSlots
        private void SideSlot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TaskbarSideSlot slot)
            {
                ApplySideSlotLayout(slot);
            }
        }

        private void ApplySideSlotLayout(TaskbarSideSlot slot)
        {
            slot.ApplyLayout(
                _placedTaskbar?.Edge ?? ScreenEdge.Left,
                SettingsService.Instance.TaskbarSideGraphDirection,
                SettingsService.Instance.TaskbarSideTitleLines);
        }

        // walks the slot row like SensorGraphRenderingGate does; (finds nothing on a horizontal taskbar)
        private void RefreshSideSlots()
        {
            VisitSideSlots(SlotsItemsControl);
        }

        private void VisitSideSlots(DependencyObject parent)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is TaskbarSideSlot slot)
                {
                    ApplySideSlotLayout(slot);
                    continue;
                }

                VisitSideSlots(child);
            }
        }

        private void OnTaskbarSideGraphDirectionChanged(string newDirection)
        {
            this.DispatcherQueue.TryEnqueue(RefreshSideSlots);
        }

        private void OnTaskbarSideTitleLinesChanged(int newLines)
        {
            this.DispatcherQueue.TryEnqueue(RefreshSideSlots);
        }


        // === taskbar tracking ===

        private bool _isTrackingTaskbar;

        // follows the taskbar while embedded; an edge move, a resolution or scaling change and an explorer.exe restart
        // all arrive as a changed snapshot
        private void StartTrackingTaskbar()
        {
            if (_isTrackingTaskbar) return;
            _isTrackingTaskbar = true;

            WinTaskbarService.Instance.TaskbarsChanged += OnTaskbarsChanged;
            WinTaskbarService.Instance.StartMonitoring();
        }

        private void StopTrackingTaskbar()
        {
            if (!_isTrackingTaskbar) return;
            _isTrackingTaskbar = false;

            WinTaskbarService.Instance.TaskbarsChanged -= OnTaskbarsChanged;
            WinTaskbarService.Instance.StopMonitoring();
        }

        // raised on the polling thread
        private void OnTaskbarsChanged(IReadOnlyList<WinTaskbarInfo> taskbars)
        {
            this.DispatcherQueue.TryEnqueue(() => FollowTaskbar(taskbars.FirstOrDefault()));
        }

        // a new taskbar handle means explorer.exe rebuilt the taskbar, maybe with the child widget, so that case
        // rebuilds; an empty snapshot mid restart is skipped
        // an open flyout is closed, not moved; the next click opens it at the new place
        private void FollowTaskbar(WinTaskbarInfo? primaryTaskbar)
        {
            if (_isClosed || !_isEmbedded || primaryTaskbar == null || primaryTaskbar == _placedTaskbar) return;

            TaskbarFlyoutWindow.CurrentInstance?.HideFlyout();

            if (primaryTaskbar.Hwnd != _taskbarHwnd)
            {
                RecreateWindow(restoreFlyout: false);
                return;
            }

            PositionOnTaskbar();
        }


        // === user interaction and composition visual states ===

        private Visual _backgroundVisual;
        private Visual _pressedVisual;
        private Visual _pressedStrokeVisual;
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
            var startOffset = primaryTaskbar != null
                ? GetStartupSlideOffset(primaryTaskbar)
                : new Vector3(0, TaskbarStartupSlideDistanceDip, 0);

            // Fluent 2 decelerate, cubic-bezier(0, 0, 0, 1)
            var easeOut = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.0f, 0.0f),
                new Vector2(0.0f, 1.0f));

            var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
            offsetAnim.InsertKeyFrame(0.0f, startOffset);
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
            var pressedStrokeBorder = FindVisualChild<Border>(TaskbarButton, "PressedStrokeBorder");
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
            if (pressedStrokeBorder != null)
            {
                _pressedStrokeVisual = ElementCompositionPreview.GetElementVisual(pressedStrokeBorder);
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

        // previous engagement snapshot; the cross-state fades run only when this flips
        private bool _wasEngaged;

        private void UpdateVisualState()
        {
            EnsureCompositionElements();
            if (_compositor == null) return;

            // stroke layers fade only across the rest/engaged boundary and snap between engaged states, so two 1px
            // edges never crossfade and flicker; fills and the content dim always fade
            bool engaged = _isPointerOver || _isPressed || _isFlyoutActive;
            bool animate = engaged != _wasEngaged;
            _wasEngaged = engaged;
            int Dur(int ms) => animate ? ms : 0;

            if (!_isFlyoutActive)
            {
                // flyout closed
                AnimateVisualOpacity(_activeHoverStrokeVisual, 0.0f, Dur(ExitStrokeDurationMs));
                AnimateVisualOpacity(_activePressedStrokeVisual, 0.0f, Dur(ExitStrokeDurationMs));

                if (_isPressed)
                {
                    AnimateVisualOpacity(_backgroundVisual, 0.0f, PressDurationMs);
                    AnimateVisualOpacity(_activeHoverVisual, 0.0f, PressDurationMs);
                    AnimateVisualOpacity(_activePressedVisual, 0.0f, PressDurationMs);
                    AnimateVisualOpacity(_pressedVisual, 1.0f, PressDurationMs);
                    // the pressed border snaps while the fill crossfades; StrokeBorder is off here, it
                    // would shift the pressed target
                    AnimateVisualOpacity(_pressedStrokeVisual, 1.0f, Dur(PressDurationMs));
                    AnimateVisualOpacity(_strokeVisual, 0.0f, Dur(PressDurationMs));
                }
                else if (_isPointerOver)
                {
                    AnimateVisualOpacity(_backgroundVisual, 1.0f, HoverBackgroundDurationMs, HoverBackgroundDelayMs);
                    AnimateVisualOpacity(_activeHoverVisual, 0.0f, HoverBackgroundDurationMs);
                    AnimateVisualOpacity(_activePressedVisual, 0.0f, HoverBackgroundDurationMs);
                    AnimateVisualOpacity(_pressedVisual, 0.0f, HoverBackgroundDurationMs);
                    AnimateVisualOpacity(_pressedStrokeVisual, 0.0f, Dur(PressDurationMs));
                    AnimateVisualOpacity(_strokeVisual, 1.0f, Dur(HoverStrokeDurationMs));
                }
                else
                {
                    AnimateVisualOpacity(_backgroundVisual, 0.0f, ExitBackgroundDurationMs);
                    AnimateVisualOpacity(_activeHoverVisual, 0.0f, ExitBackgroundDurationMs);
                    AnimateVisualOpacity(_activePressedVisual, 0.0f, ExitBackgroundDurationMs);
                    AnimateVisualOpacity(_pressedVisual, 0.0f, ExitBackgroundDurationMs);
                    AnimateVisualOpacity(_pressedStrokeVisual, 0.0f, Dur(PressDurationMs));
                    AnimateVisualOpacity(_strokeVisual, 0.0f, Dur(ExitStrokeDurationMs));
                }
            }
            else
            {
                // flyout open
                AnimateVisualOpacity(_strokeVisual, 0.0f, Dur(ExitStrokeDurationMs));
                AnimateVisualOpacity(_pressedStrokeVisual, 0.0f, Dur(PressDurationMs));

                if (_isPressed)
                {
                    AnimateVisualOpacity(_backgroundVisual, 0.0f, PressDurationMs);
                    AnimateVisualOpacity(_activeHoverVisual, 0.0f, PressDurationMs);
                    AnimateVisualOpacity(_activePressedVisual, 1.0f, PressDurationMs);
                    AnimateVisualOpacity(_pressedVisual, 0.0f, PressDurationMs);

                    AnimateVisualOpacity(_activeHoverStrokeVisual, 0.0f, Dur(PressDurationMs));
                    AnimateVisualOpacity(_activePressedStrokeVisual, 1.0f, Dur(PressDurationMs));
                }
                else if (_isPointerOver)
                {
                    AnimateVisualOpacity(_backgroundVisual, 0.0f, HoverBackgroundDurationMs, HoverBackgroundDelayMs);
                    AnimateVisualOpacity(_activeHoverVisual, 1.0f, HoverBackgroundDurationMs, HoverBackgroundDelayMs);
                    AnimateVisualOpacity(_activePressedVisual, 0.0f, HoverBackgroundDurationMs);
                    AnimateVisualOpacity(_pressedVisual, 0.0f, HoverBackgroundDurationMs);

                    AnimateVisualOpacity(_activeHoverStrokeVisual, 1.0f, Dur(HoverStrokeDurationMs));
                    AnimateVisualOpacity(_activePressedStrokeVisual, 0.0f, Dur(HoverStrokeDurationMs));
                }
                else
                {
                    // active rest; looks like hover
                    AnimateVisualOpacity(_backgroundVisual, 1.0f, ExitBackgroundDurationMs);
                    AnimateVisualOpacity(_activeHoverVisual, 0.0f, ExitBackgroundDurationMs);
                    AnimateVisualOpacity(_activePressedVisual, 0.0f, ExitBackgroundDurationMs);
                    AnimateVisualOpacity(_pressedVisual, 0.0f, ExitBackgroundDurationMs);

                    AnimateVisualOpacity(_strokeVisual, 1.0f, Dur(ExitStrokeDurationMs));
                    AnimateVisualOpacity(_activeHoverStrokeVisual, 0.0f, Dur(ExitStrokeDurationMs));
                    AnimateVisualOpacity(_activePressedStrokeVisual, 0.0f, Dur(ExitStrokeDurationMs));
                }
            }

            if (_contentVisual != null)
            {
                if (_isPressed)
                {
                    AnimateVisualOpacity(_contentVisual, PressContentOpacity, PressDurationMs);
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

            _suppressClick = false;

            // skip all drag bookkeeping while the position is locked; press feedback and click-to-toggle stay live
            if (!SettingsService.Instance.TaskbarWidgetPositionLocked && NativeMethods.GetCursorPos(out var cursorPos))
            {
                _dragTaskbar = WinTaskbarService.Instance.DiscoverNow().FirstOrDefault();
                bool isVertical = _dragTaskbar?.IsVertical ?? false;

                _dragStartCursorAlong = isVertical ? cursorPos.Y : cursorPos.X;
                _dragStartWindowAlong = isVertical ? _currentScreenRect.Y : _currentScreenRect.X;
                _isPotentialDrag = true;
                _isDragging = false;
            }

            _isPressed = true;
            // also drives the content press dim
            UpdateVisualState();
        }

        private void TaskbarButton_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPotentialDrag || !_isEmbedded || _taskbarHwnd == IntPtr.Zero) return;

            if (!NativeMethods.GetCursorPos(out var currentCursorPos)) return;

            // the drag runs along the taskbar, X on a horizontal one and Y on a vertical one
            bool isVertical = _dragTaskbar?.IsVertical ?? false;
            int delta = (isVertical ? currentCursorPos.Y : currentCursorPos.X) - _dragStartCursorAlong;

            if (!_isDragging && Math.Abs(delta) >= DragThresholdPixels)
            {
                _isDragging = true;

                // the click guard goes up at the first moved pixel: ButtonBase raises Click from its PointerReleased
                // class handler, ahead of ours, so a guard set at drag end is too late; the next press clears it
                _suppressClick = true;

                if (e != null)
                {
                    TaskbarButton.CapturePointer(e.Pointer);
                }
            }

            if (_isDragging && _dragTaskbar != null)
            {
                double scale = (_dragTaskbar.Dpi > 0 ? _dragTaskbar.Dpi : 96.0) / 96.0;
                int barStart = isVertical ? _dragTaskbar.Rect.Y : _dragTaskbar.Rect.X;
                int lengthPx = isVertical ? _currentScreenRect.Height : _currentScreenRect.Width;
                int currentAlong = isVertical ? _currentScreenRect.Y : _currentScreenRect.X;

                int targetAlong = barStart + ClampOffsetPx(_dragTaskbar, _dragStartWindowAlong + delta - barStart, lengthPx);

                if (targetAlong != currentAlong)
                {
                    _currentScreenRect = isVertical
                        ? new RectInt32(_currentScreenRect.X, targetAlong, _currentScreenRect.Width, _currentScreenRect.Height)
                        : new RectInt32(targetAlong, _currentScreenRect.Y, _currentScreenRect.Width, _currentScreenRect.Height);
                    WinTaskbarEmbedder.Position(_hwnd, _taskbarHwnd, _currentScreenRect);

                    CurrentOffsetDip = (int)Math.Round((targetAlong - barStart) / scale);
                }
            }
        }

        private void TaskbarButton_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging && e != null)
            {
                try { TaskbarButton.ReleasePointerCapture(e.Pointer); } catch { }
            }

            EndDrag();

            _isPressed = false;

            // still over the button in its new position
            bool isOverNow = false;
            if (NativeMethods.GetCursorPos(out var pt))
            {
                isOverNow = (pt.X >= _currentScreenRect.X && pt.X <= _currentScreenRect.X + _currentScreenRect.Width &&
                             pt.Y >= _currentScreenRect.Y && pt.Y <= _currentScreenRect.Y + _currentScreenRect.Height);
            }
            _isPointerOver = isOverNow;
            UpdateVisualState();
        }

        private void TaskbarButton_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            EndDrag();
            TaskbarButton_PointerExited(sender, e);
        }

        private void TaskbarButton_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            EndDrag();
            TaskbarButton_PointerExited(sender, e);
        }

        // the one place a drag is committed; release and capture loss both land here in no fixed order (or a capture
        // loss alone), the first one saves
        // (the click guard goes up in TaskbarButton_PointerMoved instead)
        private void EndDrag()
        {
            if (_isDragging)
            {
                _isDragging = false;
                SaveWindowState(wasOpen: true);
            }

            _isPotentialDrag = false;
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
