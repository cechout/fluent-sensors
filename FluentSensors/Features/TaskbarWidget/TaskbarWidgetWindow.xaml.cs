using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using WinUIEx.Messaging;

using FluentSensors.Core.Taskbar;


namespace FluentSensors.Features.TaskbarWidget
{
    // skeleton for the taskbar widget window, step 1 of Phase 3: proves the window shows up, sits
    // on top, and is clickable without stealing activation, at a fixed placeholder position
    // real taskbar-anchored placement, the poll loop, and visibility rules come in a later step,
    // once this shell itself is confirmed working
    public sealed partial class TaskbarWidgetWindow : Window
    {
        // === win32 api imports ===

        // TEMP: only for step 1, so a construction failure becomes an unmissable native dialog
        // instead of silently doing nothing; remove once the window reliably shows
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);


        // === fields ===

        private AppWindow _appWindow;
        private WindowMessageMonitor _nonActivatingMonitor; // see WinNonActivatingWindow.Apply, must stay alive
        private NativeMethods.WinEventProc _foregroundChangedCallback; // must stay alive, see constructor
        private IntPtr _foregroundHook; // released in AppWindow_Closing
        private DispatcherQueueTimer _topmostTimer; // must stay alive, see constructor
        private bool _isReassertingTopmost; // reentrancy guard, see ReassertTopmost in the constructor
        private static TaskbarWidgetWindow _retainedInstance;
        public static TaskbarWidgetWindow CurrentInstance { get; private set; }

        private int _clickCount = 0; // step 1 only, proves clicks land without needing Debug output


        // === constructor ===

        public TaskbarWidgetWindow()
        {
            // TEMP: round 3 wrapped in try/catch, round 1 and 2 both silently showed nothing with no
            // crash and no error, which is not normal WinUI 3 behavior for a real exception, strong
            // sign something was throwing during construction and getting swallowed somewhere above
            // this; this makes any such exception impossible to miss instead of guessing again
            try
            {
                this.InitializeComponent();
                CurrentInstance = this;

                _appWindow = this.AppWindow;
                _appWindow.IsShownInSwitchers = false;

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
                presenter.IsAlwaysOnTop = true;
                _appWindow.SetPresenter(presenter);

                // step 2: real taskbar-anchored placement, replaces the screen-center placeholder
                // from step 1
                // still MVP: primary taskbar only (first entry from WinTaskbarService; Shell_TrayWnd
                // is always found before any Shell_SecondaryTrayWnd, see
                // WinTaskbarService.FindAllTaskbars), End anchor hardcoded, fixed offset, one-time
                // placement at construction
                // not wired in yet, all still on the plan: poll loop, topmost reassertion, and the
                // visibility rules (fullscreen/autohide/vertical bar/...)
                const int width = 250;
                const int height = 48;
                const int offset = 8;
                var primaryTaskbar = WinTaskbarService.Instance.DiscoverNow().FirstOrDefault();

                Windows.Graphics.RectInt32 targetRect;
                if (primaryTaskbar != null)
                {
                    targetRect = TaskbarWidgetPlacement.Calculate(primaryTaskbar, TaskbarAnchor.End, offset, width, height);
                }
                else
                {
                    // no taskbar found at all (Explorer not running yet, or FindWindowExW failed);
                    // falls back to the step 1 screen-center placeholder so the window still shows
                    // up somewhere instead of at 0,0
                    var workArea = DisplayArea.Primary.WorkArea;
                    targetRect = new Windows.Graphics.RectInt32(
                        workArea.X + (workArea.Width - width) / 2,
                        workArea.Y + (workArea.Height - height) / 2,
                        width,
                        height);
                }
                _appWindow.MoveAndResize(targetRect);

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

                _appWindow.Closing += AppWindow_Closing;

                // step 3, round 3: the window never actually loses WS_EX_TOPMOST, it gets pushed below
                // the taskbar inside the topmost band whenever Explorer raises the bar on a foreground
                // change; listening for WM_WINDOWPOSCHANGED on our own window could never catch that,
                // since that message only reaches the window whose own position changed, which is why
                // round 2 did nothing and the flicker still lasted exactly one polling interval
                // SetWinEventHook fires system-wide the instant the foreground window changes, which
                // is the actual trigger, so the correction happens in the same moment instead of
                // however long the next tick takes
                // reentrancy guard: our own SetWindowPos can re-enter through the hook
                void ReassertTopmost()
                {
                    if (_isReassertingTopmost) return;
                    _isReassertingTopmost = true;
                    try
                    {
                        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                    }
                    finally
                    {
                        _isReassertingTopmost = false;
                    }
                }

                // field, not a local: native code holds the raw pointer, a local would let the GC
                // collect the delegate while the hook is still installed
                _foregroundChangedCallback = (hook, eventType, eventHwnd, idObject, idChild, thread, time) => ReassertTopmost();
                _foregroundHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _foregroundChangedCallback,
                    0, // all processes
                    0, // all threads
                    NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

                // slow safety net only, covers anything that raises the taskbar without a foreground
                // change; the hook above is what handles the normal case
                _topmostTimer = DispatcherQueue.CreateTimer();
                _topmostTimer.Interval = TimeSpan.FromSeconds(2);
                _topmostTimer.IsRepeating = true;
                _topmostTimer.Tick += (s, e) => ReassertTopmost();
                _topmostTimer.Start();

                // show completely normally first, exactly like WidgetWindow does; apply NOACTIVATE
                // only once the window is already up, the same order that worked for the MainWindow
                // test; means a brief, one-time activation flash the first time the widget appears
                // each session, acceptable for step 1, revisit once the core mechanism is confirmed
                this.Activate();
                var tempTimer = DispatcherQueue.CreateTimer();
                tempTimer.Interval = TimeSpan.FromSeconds(3);
                tempTimer.IsRepeating = false;
                tempTimer.Tick += (s, e) =>
                {
                    _nonActivatingMonitor = WinNonActivatingWindow.Apply(hwnd);
                    TestButton.Content = "Non-activating now, test clicking";
                    tempTimer.Stop();
                };
                tempTimer.Start();
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

                // both were torn down on hide, see AppWindow_Closing; the callback delegate itself
                // survives on the instance, so reinstalling is enough
                window._foregroundHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    window._foregroundChangedCallback,
                    0,
                    0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
                window._topmostTimer?.Start();

                // not yet verified on hardware: NOACTIVATE is already applied on this hwnd from the
                // first show, so Show(false) should be safe here even though it was not for the very
                // first, not-yet-rendered show further up; worth confirming once this path is reached
                window._appWindow.Show(false);
                return;
            }

            _ = new TaskbarWidgetWindow();

            // TEMP: step 1 safety net, three rounds ended in "nothing happens at all" with no way to
            // tell apart "never ran", "threw", and "ran but stayed invisible"; the constructor
            // catches its own exceptions, this covers the remaining silent case
            // fully null safe on purpose: if construction aborted midway, _appWindow can still be null
            // while CurrentInstance is already set
            var w = CurrentInstance;
            if (w?._appWindow == null || !w._appWindow.IsVisible)
            {
                MessageBoxW(IntPtr.Zero,
                    $"instance created: {w != null}\n" +
                    $"appWindow created: {w?._appWindow != null}\n" +
                    $"appWindow visible: {w?._appWindow?.IsVisible}\n" +
                    $"position: {w?._appWindow?.Position}\n" +
                    $"size: {w?._appWindow?.Size}",
                    "TEMP: widget did not become visible", 0);
            }
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

            // stop correcting z-order while hidden; both get set up again on the next ShowWidget
            _topmostTimer?.Stop();
            if (_foregroundHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }

            _appWindow.Hide();
        }
    }
}
