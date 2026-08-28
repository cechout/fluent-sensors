using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using WinUIEx;
using WinUIEx.Messaging;

using FluentSensors.Core.Taskbar;


namespace FluentSensors.Features.TaskbarWidget
{
    // the taskbar widget window, embedded as a child of Shell_TrayWnd rather than floating above it
    //
    // the earlier approach kept the window topmost and corrected its z-order whenever something covered it;
    // that could not work by design, see WinTaskbarEmbedder for why, and every correction was visible as a
    // flicker
    //
    // still MVP: primary taskbar only, End anchor hardcoded, fixed offset, placement calculated once
    // not wired in yet, all still on the plan: re-embedding after an explorer restart, reacting to taskbar
    // geometry changes, and the visibility rules (fullscreen/autohide/vertical bar)
    public sealed partial class TaskbarWidgetWindow : Window
    {
        // === win32 api imports ===

        // TEMP: so a construction or embedding failure becomes an unmissable native dialog instead of silently
        // showing nothing, which cost several rounds of guessing before; remove once this is settled
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);


        // === fields ===

        // logical pixels, scaled to the taskbars DPI before use, so these read the same at any scaling
        private const int WidgetWidthDip = 250;
        private const int AnchorOffsetDip = 8; // gap between the widget and the anchored end of the taskbar
        private const int VerticalMarginDip = 2; // gap above and below the widget, the height follows from it

        // TEMP: Start while testing, so the widget sits on the empty left side instead of fighting the system
        // tray on the right; becomes a user setting later
        private const TaskbarAnchor Anchor = TaskbarAnchor.Start;

        // confirmed same as TrafficMonitor documents for their own SetParent-into-taskbar approach: embedding
        // can fail transiently, e.g. while the start menu is open or another app is mid-embed at the same
        // moment, and normally succeeds a moment later; retrying beats reporting a false failure
        private const int MaxEmbedAttempts = 5;
        private static readonly TimeSpan EmbedRetryDelay = TimeSpan.FromMilliseconds(500);
        private int _embedAttempt;

        private AppWindow _appWindow;
        private IntPtr _hwnd;
        private IntPtr _taskbarHwnd; // parent we are embedded into, zero while detached
        private WindowMessageMonitor _nonActivatingMonitor; // see WinNonActivatingWindow.Apply, must stay alive
        private DispatcherQueueTimer _watchdogTimer; // TEMP, see constructor, must stay alive
        private bool _isEmbedded;
        private bool _embedGaveUp; // set once RetryOrReportFailure has already shown its own message
        private static TaskbarWidgetWindow _retainedInstance;
        public static TaskbarWidgetWindow CurrentInstance { get; private set; }

        private int _clickCount = 0; // proves clicks land without needing Debug output


        // === constructor ===

        public TaskbarWidgetWindow()
        {
            try
            {
                this.InitializeComponent();
                CurrentInstance = this;

                _appWindow = this.AppWindow;
                _appWindow.IsShownInSwitchers = false;
                _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

                // --- workaround: CreateForContextMenu crashes unpackaged ---
                // problem: OverlappedPresenter.CreateForContextMenu() throws a TargetInvocationException
                // in unpackaged apps (WindowsPackageType=None, which FluentSensors uses), confirmed
                // Microsoft-internal repro matches our exact csproj setup:
                // https://github.com/microsoft/microsoft-ui-xaml/issues/6765
                // fix: build the same visual result by hand via OverlappedPresenter.Create(), same
                // approach WidgetWindow already uses successfully in this project
                var presenter = OverlappedPresenter.Create();
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                // deliberately no IsAlwaysOnTop: an embedded child is ordered inside the taskbar and is not
                // part of the topmost band at all, see WinTaskbarEmbedder
                _appWindow.SetPresenter(presenter);

                // the widget sits on the taskbar, so it has to let the bar show through instead of painting a
                // rectangle of its own
                // set here, while the window is still a normal top level one: WinUIExs TransparentTintBackdrop
                // is built on DwmExtendFrameIntoClientArea and DwmEnableBlurBehindWindow, and DWM only manages
                // top level windows, so applying it after the reparent would most likely be ignored
                // NOT VERIFIED: whether the effect survives becoming a child of the taskbar at all; if the
                // widget shows up as a solid block, this is the first thing to suspect, not the XAML
                this.SystemBackdrop = new TransparentTintBackdrop();

                _appWindow.Closing += AppWindow_Closing;

                // embedding waits for Loaded rather than running straight from the constructor: reparenting a
                // window whose content is not up yet left it invisible in an earlier round, and Loaded is the
                // signal that the visual tree is actually there, instead of guessing at a delay
                ((FrameworkElement)this.Content).Loaded += (s, e) => EmbedIntoTaskbar();

                this.Activate();

                // closes the one failure mode that cost the most time before, where nothing happened at all
                // and there was no way to tell whether the code had even run
                // widened from 3s to 5s: retrying now takes up to MaxEmbedAttempts * EmbedRetryDelay before a
                // real failure is reported, the watchdog must stay quiet through all of that
                // TEMP: remove together with the MessageBox helper once embedding is settled
                _watchdogTimer = DispatcherQueue.CreateTimer();
                _watchdogTimer.Interval = TimeSpan.FromSeconds(5);
                _watchdogTimer.IsRepeating = false;
                _watchdogTimer.Tick += (s, e) =>
                {
                    _watchdogTimer.Stop();
                    if (_isEmbedded || _embedGaveUp) return; // already succeeded, or RetryOrReportFailure already reported

                    MessageBoxW(IntPtr.Zero,
                        "Window was constructed but never got embedded, Loaded probably never fired.\n\n" +
                        $"hwnd: {_hwnd}\n" +
                        $"appWindow visible: {_appWindow?.IsVisible}",
                        "TEMP: taskbar widget", 0);
                };
                _watchdogTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBoxW(IntPtr.Zero, ex.ToString(), "TEMP: TaskbarWidgetWindow construction failed", 0);
            }
        }


        // === public methods ===

        // shows the widget, reusing the previously hidden window instance if one exists instead of
        // creating a new one every time (see _retainedInstance), same pattern as WidgetWindow
        public static void ShowWidget()
        {
            if (CurrentInstance != null) return;

            if (_retainedInstance != null)
            {
                var window = _retainedInstance;
                _retainedInstance = null;
                CurrentInstance = window;

                // detached on hide, so this has to embed again rather than just show
                // not verified on hardware yet, nothing reaches this path while the widget has no close path
                window._appWindow.Show(false);
                window.EmbedIntoTaskbar();
                return;
            }

            _ = new TaskbarWidgetWindow();
        }


        // === embedding ===

        // finds the primary taskbar, works out where on it the widget belongs, and reparents into it
        // failures go through RetryOrReportFailure instead of reporting straight away, see its comment
        private void EmbedIntoTaskbar()
        {
            try
            {
                // Shell_TrayWnd is always found before any Shell_SecondaryTrayWnd, see
                // WinTaskbarService.FindAllTaskbars, so the first entry is the primary bar
                var primaryTaskbar = WinTaskbarService.Instance.DiscoverNow().FirstOrDefault();
                if (primaryTaskbar == null)
                {
                    RetryOrReportFailure("No taskbar found.");
                    return;
                }

                _taskbarHwnd = primaryTaskbar.Hwnd;

                // everything below works in physical pixels, which is what the taskbar rect and SetWindowPos
                // both use, so the logical constants get scaled once here
                double scale = primaryTaskbar.Dpi / 96.0;
                var screenRect = TaskbarWidgetPlacement.Calculate(
                    primaryTaskbar,
                    Anchor,
                    (int)(AnchorOffsetDip * scale),
                    (int)(WidgetWidthDip * scale),
                    (int)(VerticalMarginDip * scale));

                if (!WinTaskbarEmbedder.Embed(_hwnd, _taskbarHwnd, screenRect, out int errorCode))
                {
                    _taskbarHwnd = IntPtr.Zero;
                    RetryOrReportFailure($"SetParent failed, win32 error {errorCode}.");
                    return;
                }

                // possibly redundant now: a child windows clicks activate its top level ancestor, which is the
                // taskbar rather than us, so there may be no activation left to suppress
                // kept because it is verified working and costs one call, worth removing once confirmed unneeded
                _nonActivatingMonitor = WinNonActivatingWindow.Apply(_hwnd);

                _embedAttempt = 0;
                _isEmbedded = true;
                TestButton.Content = "Embedded, test clicking";
            }
            catch (Exception ex)
            {
                RetryOrReportFailure(ex.ToString());
            }
        }

        // retries embedding a few times before giving up, confirmed the same strategy TrafficMonitor uses for
        // its own SetParent-into-taskbar approach: a transient failure (start menu open, another app mid-embed
        // at the same moment) usually clears up a moment later on its own
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

            MessageBoxW(IntPtr.Zero,
                $"Embedding failed after {_embedAttempt} attempts.\n\n{reason}",
                "TEMP: taskbar widget", 0);
            _embedGaveUp = true;
        }


        // === user interaction ===

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            _clickCount++;
            TestButton.Content = $"Clicked {_clickCount}x";
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

            // detach before hiding so AppWindow keeps operating on a plain top level window while the widget
            // is away; ShowWidget embeds again on the way back
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
