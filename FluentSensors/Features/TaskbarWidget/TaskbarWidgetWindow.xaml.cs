using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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
        private const int SensorSlotWidthDip = 90; // width per pinned sensor slot
        private const int SensorSlotSpacingDip = 4; // spacing between sensor slots
        private const int ButtonPaddingDip = 4; // inner horizontal padding of the taskbar button
        private const int MinimumWidgetWidthDip = 60; // fallback width when no sensors are pinned

        // Start while testing; will become a user setting later
        private const TaskbarAnchor Anchor = TaskbarAnchor.Start;

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
                double scale = primaryTaskbar.Dpi / 96.0;
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


        // === user interaction ===

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
