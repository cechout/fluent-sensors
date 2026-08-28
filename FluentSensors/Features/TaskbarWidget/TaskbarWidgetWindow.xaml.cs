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

        private const int WidgetWidth = 250;
        private const int WidgetHeight = 48;
        private const int AnchorOffset = 8; // gap between the widget and the anchored end of the taskbar

        private AppWindow _appWindow;
        private IntPtr _hwnd;
        private IntPtr _taskbarHwnd; // parent we are embedded into, zero while detached
        private WindowMessageMonitor _nonActivatingMonitor; // see WinNonActivatingWindow.Apply, must stay alive
        private DispatcherQueueTimer _watchdogTimer; // TEMP, see constructor, must stay alive
        private bool _isEmbedded;
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

                _appWindow.Closing += AppWindow_Closing;

                // embedding waits for Loaded rather than running straight from the constructor: reparenting a
                // window whose content is not up yet left it invisible in an earlier round, and Loaded is the
                // signal that the visual tree is actually there, instead of guessing at a delay
                ((FrameworkElement)this.Content).Loaded += (s, e) => EmbedIntoTaskbar();

                this.Activate();

                // TEMP: closes the one failure mode that cost the most time before, where nothing happened at
                // all and there was no way to tell whether the code had even run
                // remove together with the MessageBox helper once embedding is settled
                _watchdogTimer = DispatcherQueue.CreateTimer();
                _watchdogTimer.Interval = TimeSpan.FromSeconds(3);
                _watchdogTimer.IsRepeating = false;
                _watchdogTimer.Tick += (s, e) =>
                {
                    _watchdogTimer.Stop();
                    if (_isEmbedded) return;

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
        private void EmbedIntoTaskbar()
        {
            try
            {
                // Shell_TrayWnd is always found before any Shell_SecondaryTrayWnd, see
                // WinTaskbarService.FindAllTaskbars, so the first entry is the primary bar
                var primaryTaskbar = WinTaskbarService.Instance.DiscoverNow().FirstOrDefault();
                if (primaryTaskbar == null)
                {
                    MessageBoxW(IntPtr.Zero,
                        "No taskbar found, nothing to embed into.",
                        "TEMP: taskbar widget", 0);
                    return;
                }

                _taskbarHwnd = primaryTaskbar.Hwnd;
                var screenRect = TaskbarWidgetPlacement.Calculate(
                    primaryTaskbar, TaskbarAnchor.End, AnchorOffset, WidgetWidth, WidgetHeight);

                if (!WinTaskbarEmbedder.Embed(_hwnd, _taskbarHwnd, screenRect, out int errorCode))
                {
                    _taskbarHwnd = IntPtr.Zero;
                    MessageBoxW(IntPtr.Zero,
                        $"SetParent into the taskbar failed.\n\n" +
                        $"win32 error: {errorCode}\n" +
                        $"taskbar hwnd: {primaryTaskbar.Hwnd}\n" +
                        $"target rect: {screenRect.X},{screenRect.Y} {screenRect.Width}x{screenRect.Height}",
                        "TEMP: taskbar widget", 0);
                    return;
                }

                // possibly redundant now: a child windows clicks activate its top level ancestor, which is the
                // taskbar rather than us, so there may be no activation left to suppress
                // kept because it is verified working and costs one call, worth removing once confirmed unneeded
                _nonActivatingMonitor = WinNonActivatingWindow.Apply(_hwnd);

                _isEmbedded = true;
                TestButton.Content = "Embedded, test clicking";
            }
            catch (Exception ex)
            {
                MessageBoxW(IntPtr.Zero, ex.ToString(), "TEMP: embedding failed", 0);
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
