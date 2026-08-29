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
using System.Runtime.InteropServices;
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
    // as a child of the taskbar there is no z-order contest; the window belongs to the taskbar directly
    public sealed partial class TaskbarWidgetWindow : Window
    {
        // === win32 api imports ===

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);


        // === fields ===

        // logical pixels, scaled to the taskbars DPI before use, so these read the same at any scaling
        private const int VerticalMarginDip = 2; // gap above and below the widget; height follows from it
        private const int AnchorOffsetDip = 8; // gap between the widget and the anchored end of the taskbar
        private const int SensorSlotWidthDip = 120; // width per pinned sensor slot
        private const int SensorSlotSpacingDip = 8; // spacing between sensor slots
        private const int ButtonPaddingDip = 0; // inner horizontal padding of the taskbar button
        private const int MinimumWidgetWidthDip = 60; // fallback width when no sensors are pinned

        // Start while testing; will become a user setting later
        private const TaskbarAnchor Anchor = TaskbarAnchor.Start;

        // --- taskbar button animation timings (in milliseconds) ---
        private const int HoverBackgroundDelayMs = 0; // delay before hover background starts (Standard Windows: 0ms)
        private const int HoverBackgroundDurationMs = 83; // duration of hover background fade-in (Standard Windows: 83ms [ControlFasterAnimationDuration])
        private const int ExitBackgroundDurationMs = 167; // duration of background fade-out on exit (Standard Windows: 167ms [ControlFastAnimationDuration])
        private const int ExitStrokeDurationMs = 83; // duration of border stroke fade-out on exit (Standard Windows: 83ms [ControlFasterAnimationDuration])
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
        private static TaskbarWidgetWindow _retainedInstance;
        public static TaskbarWidgetWindow CurrentInstance { get; private set; }

        public TaskbarWidgetViewModel ViewModel { get; }


        // === constructor ===

        public TaskbarWidgetWindow() : this(ResolveSensors(SensorSelectionService.Instance.GetSelection(SensorSelectionProfile.Taskbar)))
        {
        }

        public TaskbarWidgetWindow(List<SensorRowViewModel> selectedSensors)
        {
            try
            {
                ViewModel = new TaskbarWidgetViewModel(selectedSensors);
                this.InitializeComponent();
                CurrentInstance = this;

                _appWindow = this.AppWindow;
                _appWindow.IsShownInSwitchers = false;
                _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

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
                // set here, while the window is still a normal top level one: WinUIExs TransparentTintBackdrop
                // is built on DwmExtendFrameIntoClientArea and DwmEnableBlurBehindWindow, and DWM only manages top level windows
                this.SystemBackdrop = new TransparentTintBackdrop();

                // move offscreen before initial activation so no un-embedded frame or border flashes on the desktop
                _appWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));

                _appWindow.Closing += AppWindow_Closing;

                // wire left-button press/release animations even when Button internally handles clicks
                TaskbarButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(TaskbarButton_PointerPressed), true);
                TaskbarButton.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(TaskbarButton_PointerReleased), true);

                TaskbarButton.Loaded += (s, e) =>
                {
                    TaskbarButton.ApplyTemplate();
                    EnsureCompositionElements();
                };

                // embedding waits for Loaded rather than running straight from the constructor: reparenting a window
                // whose content is not up yet left it invisible; Loaded guarantees the visual tree is actually there
                ((FrameworkElement)this.Content).Loaded += (s, e) => EmbedIntoTaskbar();

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
                CurrentInstance.Activate();
                return;
            }

            if (_retainedInstance != null)
            {
                var window = _retainedInstance;
                _retainedInstance = null;
                CurrentInstance = window;

                window.ReconfigureFor(selectedSensors);
                window.ViewModel.SetLiveDataActive(true);
                window.SetGraphsRenderingActive(true);

                // detached on hide, so this embeds again rather than just showing
                window._appWindow.Show(false);
                window.EmbedIntoTaskbar();
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
                double scale = primaryTaskbar.Dpi / 96.6;
                var screenRect = TaskbarWidgetPlacement.Calculate(
                    primaryTaskbar,
                    Anchor,
                    (int)(AnchorOffsetDip * scale),
                    (int)(widthDip * scale),
                    (int)(VerticalMarginDip * scale));

                if (!WinTaskbarEmbedder.Embed(_hwnd, _taskbarHwnd, screenRect, out int errorCode))
                {
                    _taskbarHwnd = IntPtr.Zero;
                    RetryOrReportFailure($"SetParent failed with Win32 error {errorCode}");
                    return;
                }

                // suppresses focus stealing on click via WM_MOUSEACTIVATE returning MA_NOACTIVATE
                _nonActivatingMonitor = WinNonActivatingWindow.Apply(_hwnd);

                // Win32-level mouse tracking: ensures the very first hover triggers instantly without needing a prior click
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
            var screenRect = TaskbarWidgetPlacement.Calculate(
                primaryTaskbar,
                Anchor,
                (int)(AnchorOffsetDip * scale),
                (int)(widthDip * scale),
                (int)(VerticalMarginDip * scale));

            WinTaskbarEmbedder.Position(_hwnd, _taskbarHwnd, screenRect);
        }

        // retries embedding a few times before giving up: a transient failure (start menu open, another app mid-embed)
        // usually clears up a moment later on its own
        private void RetryOrReportFailure(string reason)
        {
            _embedAttempt++;
            if (_embedAttempt < MaxEmbedAttempts)
            {
                var retryTimer = DispatcherQueue.CreateTimer();
                retryTimer.Interval = EmbedRetryDelay;
                retryTimer.IsRepeating = false;
                retryTimer.Tick += (s, e) =>
                {
                    retryTimer.Stop();
                    EmbedIntoTaskbar();
                };
                retryTimer.Start();
                return;
            }

            ShowErrorMessage("Fluent Sensors", $"The taskbar widget could not be attached to the taskbar.\n\nDetails: {reason}");
            _embedGaveUp = true;
        }

        private static void ShowErrorMessage(string title, string message)
        {
            MessageBoxW(IntPtr.Zero, message, title, 0x00000010 /* MB_ICONERROR */);
        }

        // calculates total DIP width: sensor slots + inter-slot spacing + button padding + extra buffer for chart margins and DPI scaling
        private int CalculateWidgetWidthDip(int sensorCount)
        {
            if (sensorCount <= 0) return MinimumWidgetWidthDip;
            int itemsWidth = (sensorCount * SensorSlotWidthDip) + (Math.Max(0, sensorCount - 1) * SensorSlotSpacingDip);
            return itemsWidth + (ButtonPaddingDip * 2) + 8;
        }

        private static List<SensorRowViewModel> ResolveSensors(IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count == 0) return new List<SensorRowViewModel>();

            var allSensors = SensorsViewModel.Instance.HardwareGroups
                .SelectMany(g => g.Sensors.Concat(g.HiddenSensors));

            return ids
                .Select(id => allSensors.FirstOrDefault(s => s.Id == id))
                .Where(s => s != null)
                .ToList();
        }

        private void SetGraphsRenderingActive(bool active)
        {
            if (this.Content is DependencyObject root)
            {
                SensorGraphRenderingGate.SetActive(root, active);
            }
        }


        // === user interaction & directcomposition animations ===

        private Visual _backgroundVisual;
        private Visual _pressedVisual;
        private Visual _strokeVisual;
        private Visual _contentVisual;
        private Compositor _compositor;
        private bool _isPointerOver;

        private void EnsureCompositionElements()
        {
            if (_backgroundVisual != null) return;

            TaskbarButton.ApplyTemplate();

            var bgBorder = FindVisualChild<Border>(TaskbarButton, "BackgroundBorder");
            var pressedBorder = FindVisualChild<Border>(TaskbarButton, "PressedBorder");
            var strokeBorder = FindVisualChild<Border>(TaskbarButton, "StrokeBorder");
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
            if (strokeBorder != null)
            {
                _strokeVisual = ElementCompositionPreview.GetElementVisual(strokeBorder);
            }
            if (contentPresenter != null)
            {
                _contentVisual = ElementCompositionPreview.GetElementVisual(contentPresenter);
            }
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

            // instant crisp border stroke on hover (0ms)
            if (_strokeVisual != null)
            {
                _strokeVisual.Opacity = 1.0f;
            }

            // smooth background fade-in with configurable delay and duration
            if (_backgroundVisual != null)
            {
                var anim = _compositor.CreateScalarKeyFrameAnimation();
                anim.InsertKeyFrame(0.0f, 0.0f);
                anim.InsertKeyFrame(1.0f, 1.0f);
                if (HoverBackgroundDelayMs > 0)
                {
                    anim.DelayTime = TimeSpan.FromMilliseconds(HoverBackgroundDelayMs);
                }
                anim.Duration = TimeSpan.FromMilliseconds(HoverBackgroundDurationMs);
                _backgroundVisual.StartAnimation("Opacity", anim);
            }
        }

        private void TaskbarButton_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isPointerOver = false;
            EnsureCompositionElements();
            if (_compositor == null) return;

            if (_pressedVisual != null)
            {
                _pressedVisual.Opacity = 0.0f;
            }

            // smooth background fade-out on exit
            if (_backgroundVisual != null)
            {
                var bgAnim = _compositor.CreateScalarKeyFrameAnimation();
                bgAnim.InsertKeyFrame(1.0f, 0.0f);
                bgAnim.Duration = TimeSpan.FromMilliseconds(ExitBackgroundDurationMs);
                _backgroundVisual.StartAnimation("Opacity", bgAnim);
            }

            // smooth stroke fade-out on exit
            if (_strokeVisual != null)
            {
                var strokeAnim = _compositor.CreateScalarKeyFrameAnimation();
                strokeAnim.InsertKeyFrame(1.0f, 0.0f);
                strokeAnim.Duration = TimeSpan.FromMilliseconds(ExitStrokeDurationMs);
                _strokeVisual.StartAnimation("Opacity", strokeAnim);
            }

            if (_contentVisual != null)
            {
                _contentVisual.Opacity = 1.0f;
            }
        }

        private void TaskbarButton_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var ptr = e?.GetCurrentPoint(TaskbarButton);
            if (ptr != null && !ptr.Properties.IsLeftButtonPressed) return;

            EnsureCompositionElements();

            // hide hover background so ONLY the pressed background is rendered (prevents double-layering)
            if (_backgroundVisual != null)
            {
                _backgroundVisual.Opacity = 0.0f;
            }

            // show pressed background
            if (_pressedVisual != null)
            {
                _pressedVisual.Opacity = 1.0f;
            }

            // content press feedback: 95% opacity
            if (_contentVisual != null && _compositor != null)
            {
                var pressAnim = _compositor.CreateScalarKeyFrameAnimation();
                pressAnim.InsertKeyFrame(1.0f, 0.95f);
                pressAnim.Duration = TimeSpan.FromMilliseconds(PressDurationMs);
                _contentVisual.StartAnimation("Opacity", pressAnim);
            }
        }

        private void TaskbarButton_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EnsureCompositionElements();

            if (_pressedVisual != null)
            {
                _pressedVisual.Opacity = 0.0f;
            }

            // restore hover background if still hovered
            if (_backgroundVisual != null && _isPointerOver)
            {
                _backgroundVisual.Opacity = 1.0f;
            }

            if (_contentVisual != null && _compositor != null)
            {
                var relAnim = _compositor.CreateScalarKeyFrameAnimation();
                relAnim.InsertKeyFrame(1.0f, 1.0f);
                relAnim.Duration = TimeSpan.FromMilliseconds(PressDurationMs);
                _contentVisual.StartAnimation("Opacity", relAnim);
            }
        }

        private void TaskbarButton_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            TaskbarButton_PointerExited(sender, e);
        }

        private void TaskbarButton_Click(object sender, RoutedEventArgs e)
        {
            // opens animated popup flyout window above taskbar widget
        }


        // === lifecycle ===

        // --- memory leak: TaskbarWidgetWindow never released after close ---
        // problem: WinUI 3 never releases secondary Window objects back to the GC/OS after a real close
        // confirmed, still-open platform bug, reproducible even with empty window content:
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // fix: hide instead of actually closing, and keep this instance around (_retainedInstance) for reuse
        // same approach as WidgetWindow
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            args.Cancel = true;
            CurrentInstance = null;
            _retainedInstance = this;

            SetGraphsRenderingActive(false);
            ViewModel?.SetLiveDataActive(false);

            // detach before hiding so AppWindow keeps operating on a plain top level window while the widget is away
            if (_taskbarHwnd != IntPtr.Zero)
            {
                WinTaskbarEmbedder.Detach(_hwnd);
                _taskbarHwnd = IntPtr.Zero;
                _isEmbedded = false;
            }

            _appWindow.Hide();
        }
    }
}
