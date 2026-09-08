using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WinRT;

using FluentSensors.Controls.SensorRow;
using FluentSensors.Features.TaskbarWidget;
using FluentSensors.Persistence.Models;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.CsvLogging
{
    // small always-on-top readout for a csv recording: start, stop, and how long it has been running, how many
    // sensors it covers and how many rows it has written so far
    //
    // chrome, backdrop and the retained-instance lifecycle are the same recipe WidgetWindow uses; there is no shared
    // window base in this project, every window carries its own copy
    public sealed partial class CsvLoggerWindow : Window
    {
        // === win32 api imports ===

        // import the Windows-API to calculate the screen scaling (100%, 125%, 150% etc.)
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);


        // === fields ===

        private AppWindow _appWindow;
        private const string WindowKey = "CsvLogger";

        // fixed window width in XAML DIP; the height is always measured from the content instead, so collapsing the
        // lower region, hiding the status line, or a wrap panel that breaks into another row all resize correctly
        // without a second hand-tuned number
        private const double WindowWidthDip = 240;
        private const double FallbackWindowHeightDip = 232;

        // status line under main bar
        private const bool ShowStatusLine = false;

        // whether the lower region (readout and save location) is currently shown; pure view state, the recording
        // itself does not care
        private bool _isExpanded = true;

        public CsvLoggerViewModel ViewModel { get; }
        public static CsvLoggerWindow CurrentInstance { get; private set; }
        private static CsvLoggerWindow _retainedInstance;

        private bool _isClosed = false;
        private static bool _isRecreating = false;

        // system backdrop controllers and configuration
        private DesktopAcrylicController _acrylicController;
        private MicaController _micaController;
        private SystemBackdropConfiguration _configurationSource;
        private Windows.UI.ViewManagement.UISettings? _uiSettings;


        // === constructor ===

        public CsvLoggerWindow()
        {
            // initialization
            ViewModel = new CsvLoggerViewModel();
            this.InitializeComponent();
            this.AppWindow.SetIcon("Assets\\Icon\\Icon.ico");
            CurrentInstance = this;

            // window configuration
            _appWindow = this.AppWindow;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomTitleBar);
            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = true; // same as the widget, a running recording has to stay readable over other apps
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
            presenter.IsResizable = false;
            _appWindow.SetPresenter(presenter);

            // content state has to be applied before the first measure, both the status line and the expand state
            // change how tall the window ends up
            StatusTextBlock.Visibility = ShowStatusLine ? Visibility.Visible : Visibility.Collapsed;
            ApplyExpandState();

            // window size and position:
            // the width is fixed and the height comes from the content, only the position is restored, and only
            // when it still lands on a connected monitor; without a usable saved position Windows places the
            // window itself
            ApplyWindowSize();
            RestoreWindowPosition();
            SaveWindowState();

            // theming
            SetBackdrop(SettingsService.Instance.BackdropType);
            ApplyTheme(SettingsService.Instance.AppTheme);

            // event routing
            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            SettingsService.Instance.BackdropTypeChanged += OnBackdropTypeChanged;
            SettingsService.Instance.OpacityChanged += OnOpacityChanged;
            SettingsService.Instance.TintColorChanged += OnTintColorChanged;

            try
            {
                _uiSettings = new Windows.UI.ViewManagement.UISettings();
                TaskbarFlyoutWindow.SeedSystemVisualsSnapshot(_uiSettings);
                _uiSettings.AdvancedEffectsEnabledChanged += OnSystemVisualSettingsChanged;
                _uiSettings.ColorValuesChanged += OnSystemVisualSettingsChanged;
            }
            catch { }

            // the status line is the one piece of content whose height is not fixed, a long file name wraps it onto
            // another line; re-measuring on every change keeps the window exactly as tall as it needs to be
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            this.Closed += CsvLoggerWindow_Closed;
            _appWindow.Changed += AppWindow_Changed;
            _appWindow.Closing += AppWindow_Closing;

            KickBackdropRefresh();
        }


        // === public methods ===

        // opens the logger with the given sensors, reusing the previously hidden window instance if one exists
        // instead of creating a new one every time (see _retainedInstance), same pattern as WidgetWindow
        //
        // a running recording keeps the sensor set it started with; its columns are already written into the open
        // files header, so the push is dropped and the window only says why
        public static void ShowWithSensors(List<SensorRowViewModel> selectedSensors)
        {
            bool isSelectionLocked = CsvLoggingService.Instance.IsRunning;
            if (!isSelectionLocked)
            {
                CsvLoggingService.Instance.SetSensors(selectedSensors);
            }

            // logger is already open: nothing to build, just bring it back up
            if (CurrentInstance != null)
            {
                if (isSelectionLocked)
                {
                    CurrentInstance.ViewModel.ShowSelectionLockedHint();
                }

                CurrentInstance.RestoreAndActivate();
                return;
            }

            // logger was previously hidden (closed via the X button): reuse that native window instead of creating
            // a new one
            if (_retainedInstance != null)
            {
                var window = _retainedInstance;
                _retainedInstance = null;
                CurrentInstance = window;

                window.ViewModel.SetReadoutActive(true);
                if (isSelectionLocked)
                {
                    window.ViewModel.ShowSelectionLockedHint();
                }

                window.RestoreAndActivate();
                return;
            }

            // no logger has been created this session yet: build a fresh native window
            var newWindow = new CsvLoggerWindow();
            if (isSelectionLocked)
            {
                newWindow.ViewModel.ShowSelectionLockedHint();
            }
            newWindow.Activate();
        }

        public void SafeDestroy()
        {
            if (_isClosed) return;
            _isClosed = true;

            try
            {
                SettingsService.Instance.ThemeChanged -= OnThemeChanged;
                SettingsService.Instance.BackdropTypeChanged -= OnBackdropTypeChanged;
                SettingsService.Instance.OpacityChanged -= OnOpacityChanged;
                SettingsService.Instance.TintColorChanged -= OnTintColorChanged;
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
            // CsvLoggerWindow_Closed stays attached, it carries the real teardown once the close completes
            try
            {
                _appWindow.Closing -= AppWindow_Closing;
                _appWindow.Changed -= AppWindow_Changed;
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
            catch { }

            try
            {
                _acrylicController?.Dispose();
                _acrylicController = null;
                _micaController?.Dispose();
                _micaController = null;
                this.Close();
            }
            catch { }
        }

        // fully destroys and recreates the logger window when Windows global transparency or theme is toggled
        //
        // safe to do mid-recording: CsvLoggingService owns the open file and the counters, the rebuilt window just
        // reads them again
        public static void RecreateWindow()
        {
            if (_isRecreating) return;
            if (CurrentInstance == null && _retainedInstance == null) return;
            _isRecreating = true;

            bool wasVisible = CurrentInstance != null && CurrentInstance._appWindow != null && CurrentInstance._appWindow.IsVisible;

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

            if (wasVisible)
            {
                // the queue needs a fallback: closing the main window to the tray leaves MainWindow.CurrentInstance
                // null, and a null-conditional TryEnqueue there would skip the finally below and latch _isRecreating
                // for the rest of the session, so the logger would never rebuild again
                var queue = MainWindow.CurrentInstance?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
                bool queued = queue != null && queue.TryEnqueue(() =>
                {
                    try
                    {
                        var window = new CsvLoggerWindow();
                        window.Activate();
                    }
                    finally
                    {
                        _isRecreating = false;
                    }
                });

                if (!queued)
                {
                    _isRecreating = false;
                }
            }
            else
            {
                _isRecreating = false;
            }
        }


        // === lifecycle ===

        private void CsvLoggerWindow_Closed(object sender, WindowEventArgs args)
        {
            SaveWindowState();

            // we detach the event handlers from the settings service
            SettingsService.Instance.BackdropTypeChanged -= OnBackdropTypeChanged;
            SettingsService.Instance.OpacityChanged -= OnOpacityChanged;
            SettingsService.Instance.TintColorChanged -= OnTintColorChanged;
            SettingsService.Instance.ThemeChanged -= OnThemeChanged;

            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.Cleanup();

            _acrylicController?.Dispose();
            _acrylicController = null;
            _micaController?.Dispose();
            _micaController = null;

            this.Activated -= Window_Activated;
            _configurationSource = null;
            CurrentInstance = null;
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_configurationSource != null)
            {
                // always render the active blur, otherwise clicking outside the logger drops it to the inactive
                // material while it is still sitting on top of everything
                _configurationSource.IsInputActive = true;
            }
        }

        // --- memory leak: CsvLoggerWindow never released after close ---
        // problem: WinUI 3 never releases secondary Window objects back to the GC/OS after a real close
        // confirmed, still-open platform bug, reproducible even with empty window content:
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // fix: hide instead of actually closing, and keep this instance around (_retainedInstance) for reuse the next
        // time the logger is opened
        // same approach as WidgetWindow and HiddenSensorsWindow
        //
        // a running recording is deliberately left alone here; it lives in CsvLoggingService, so closing the readout
        // only stops the readout, never the logging
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // SafeDestroy is tearing this instance down: let the close proceed instead of handing a window with
            // _isClosed set back to _retainedInstance, where every later SetBackdrop and ApplyTheme returns early
            if (_isClosed) return;

            args.Cancel = true;

            SaveWindowState();
            CurrentInstance = null;
            _retainedInstance = this;

            _appWindow.Hide();
            ViewModel.SetReadoutActive(false);
        }

        private void OnViewModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CsvLoggerViewModel.StatusText)) return;
            if (_isClosed) return;

            ApplyWindowSize();
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            // minimize/restore never sets DidPresenterChange, that one only fires when the Presenter itself is
            // swapped; a state change within the same OverlappedPresenter shows up as DidSizeChange instead
            if (args.DidSizeChange)
            {
                bool isMinimized = sender.Presenter is OverlappedPresenter presenter &&
                                   presenter.State == OverlappedPresenterState.Minimized;
                ViewModel.SetReadoutActive(!isMinimized);
            }

            if (args.DidPositionChange && this.AppWindow.IsVisible)
            {
                SaveWindowState();
            }
        }


        // === user interaction ===

        private void StartLogging_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Start();
        }

        private void StopLogging_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Stop();
        }

        // collapses the window down to the divider, so a running recording can sit on screen as a thin bar with just
        // the stop button on it
        private void ToggleDetails_Click(object sender, RoutedEventArgs e)
        {
            _isExpanded = !_isExpanded;

            ApplyExpandState();
            ApplyWindowSize();
            SaveWindowState();
        }

        private void PickLogFolder_Click(object sender, RoutedEventArgs e)
        {
            // the dialog is owned by this window so it cannot end up behind an always-on-top logger
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            string picked = Win32FileDialogHelper.PickFolder(hwnd, "CSV Logging Folder", ViewModel.LogFolderText);
            if (picked == null) return; // user cancelled

            ViewModel.SetLogFolder(picked);
        }

        private void BackToDashboard_Click(object sender, RoutedEventArgs e)
        {
            // check if the main window instance exists in memory
            if (MainWindow.CurrentInstance != null)
            {
                MainWindow.CurrentInstance.OpenDashboard();
            }
            else
            {
                // if the instance was completely destroyed, create a new one
                // the app process is safely kept alive by the open logger window
                var newMainWindow = new MainWindow();
                newMainWindow.Activate();
            }
        }


        // === settings event listeners and handlers ===

        private void OnThemeChanged(string newTheme)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                ApplyTheme(newTheme);
            });
        }

        private void OnBackdropTypeChanged(string newType)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                SetBackdrop(newType);
            });
        }

        private void OnOpacityChanged(float tintOpacity, float luminosityOpacity)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                UpdateAcrylicProperties();
            });
        }

        private void OnTintColorChanged(bool useAccentColor, Windows.UI.Color customColor)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                UpdateAcrylicProperties();
                UpdateSolidBackground();
            });
        }

        // a named handler rather than a lambda, so SafeDestroy can detach it again; every rebuilt window subscribes
        // anew and without the detach the UISettings handler list grows by one per rebuild
        //
        // routes to RouteSystemVisualsChange rather than a rebuild directly, so a pure accent change resolves into
        // an in-place refresh instead
        private void OnSystemVisualSettingsChanged(Windows.UI.ViewManagement.UISettings sender, object args)
        {
            TaskbarFlyoutWindow.RouteSystemVisualsChange(sender);
        }


        // === window sizing and positioning ===

        // converts the screen DPI to a scale factor
        // (100% = 1.0, 125% = 1.25, etc.)
        private double GetScaleFactor()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            return dpi / 96.0;
        }

        // shows or hides the lower region and points the chevron at what the next click will do
        private void ApplyExpandState()
        {
            LowerRegion.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;

            // segoe fluent icons ChevronUp and ChevronDown, escaped rather than pasted so this file stays
            // plain ascii like the rest of the sources
            ToggleDetailsIcon.Glyph = _isExpanded ? "\uE70E" : "\uE70D";
            ToolTipService.SetToolTip(ToggleDetailsButton, _isExpanded ? "Hide details" : "Show details");
        }

        // measures the content at the fixed window width and sizes the window to exactly that
        // ResizeClient rather than Resize: with ExtendsContentIntoTitleBar the client area is the whole content, so
        // this needs no assumption about how thick the window frame currently is
        private void ApplyWindowSize()
        {
            double scaleFactor = GetScaleFactor();

            RootGrid.InvalidateMeasure();
            RootGrid.Measure(new Windows.Foundation.Size(WindowWidthDip, double.PositiveInfinity));

            // a measure before the content is ready can come back empty; the fallback keeps a usable window instead
            // of a zero-height one
            double heightDip = RootGrid.DesiredSize.Height;
            if (heightDip <= 0) heightDip = FallbackWindowHeightDip;

            _appWindow.ResizeClient(new Windows.Graphics.SizeInt32(
                (int)(WindowWidthDip * scaleFactor),
                (int)(heightDip * scaleFactor)));
        }

        private void RestoreWindowPosition()
        {
            var savedState = WindowStateService.Instance.GetState(WindowKey);
            if (savedState == null) return;
            if (!IsPositionOnScreen(savedState.X, savedState.Y, _appWindow.Size.Width, _appWindow.Size.Height)) return;

            _appWindow.Move(new Windows.Graphics.PointInt32(savedState.X, savedState.Y));
        }

        private void RestoreAndActivate()
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Restore();
            }

            _appWindow.Show();
            this.Activate();
        }

        // checks whether the given rect would actually be visible on any currently connected monitor; a saved position
        // can become stale if the monitor it was on gets disconnected, or the display arrangement changes
        private bool IsPositionOnScreen(int x, int y, int width, int height)
        {
            var rect = new Windows.Graphics.RectInt32(x, y, width, height);

            // indexed loop instead of foreach: iterating DisplayArea.FindAll() with foreach throws an
            // InvalidCastException due to a WinRT interop bug in its enumerator; indexer access avoids it
            var displayAreas = DisplayArea.FindAll();
            for (int i = 0; i < displayAreas.Count; i++)
            {
                if (RectsOverlap(rect, displayAreas[i].WorkArea))
                {
                    return true;
                }
            }
            return false;
        }

        private bool RectsOverlap(Windows.Graphics.RectInt32 a, Windows.Graphics.RectInt32 b)
        {
            return a.X < b.X + b.Width && a.X + a.Width > b.X &&
                   a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
        }

        // writes the current rect (debounced) to the window state store
        // WasOpen is deliberately left untouched: unlike the widget, the logger never reopens itself on the next
        // launch, a recording is always started by hand
        private void SaveWindowState()
        {
            var state = WindowStateService.Instance.GetState(WindowKey) ?? new WindowState();

            // while minimized, Windows reports the windows position as the (-32000, -32000) sentinel value; keep the
            // last known real rect instead of overwriting it with that garbage
            bool isMinimized = this.AppWindow.Presenter is OverlappedPresenter presenter &&
                               presenter.State == OverlappedPresenterState.Minimized;
            if (!isMinimized)
            {
                state.X = _appWindow.Position.X;
                state.Y = _appWindow.Position.Y;
                state.Width = _appWindow.Size.Width;
                state.Height = _appWindow.Size.Height;
            }

            WindowStateService.Instance.SetState(WindowKey, state);
        }


        // === theme and backdrop application ===

        private void ApplyTheme(string themeTag)
        {
            if (_isClosed) return;

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
                    "Light" => TitleBarTheme.Light,
                    "Dark" => TitleBarTheme.Dark,
                    _ => TitleBarTheme.UseDefaultAppMode
                };
            }
        }

        private void UpdateAcrylicProperties()
        {
            if (_isClosed) return;

            if (_acrylicController != null)
            {
                Windows.UI.Color targetColor;
                if (SettingsService.Instance.UseAccentColor)
                {
                    targetColor = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
                }
                else
                {
                    targetColor = SettingsService.Instance.CustomTintColor;
                }

                _acrylicController.TintColor = targetColor;
                _acrylicController.TintOpacity = SettingsService.Instance.TintOpacity;
                _acrylicController.LuminosityOpacity = SettingsService.Instance.LuminosityOpacity;
            }
        }

        private void UpdateSolidBackground()
        {
            if (_isClosed) return;

            // we intervene only, if "solid" is selected
            if (SettingsService.Instance.BackdropType == "None")
            {
                Windows.UI.Color targetColor = SettingsService.Instance.UseAccentColor
                    ? (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]
                    : SettingsService.Instance.CustomTintColor;

                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(targetColor);
            }
        }

        // re-reads the live SystemAccentColor into the acrylic tint and the solid background, for a pure OS accent
        // change; both already resolve the accent fresh on every call, so no window rebuild is needed
        public void RefreshAccentSurfaces()
        {
            UpdateAcrylicProperties();
            UpdateSolidBackground();
        }

        // dynamically applies the chosen backdrop material based on the users selection in the settings page
        // setup follows the official Microsoft guide for system backdrops in XAML apps:
        // https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops
        public void SetBackdrop(string backdropType)
        {
            if (_isClosed) return;

            DispatcherQueue.EnsureSystemDispatcherQueue();

            if (_configurationSource == null)
            {
                _configurationSource = new SystemBackdropConfiguration();
                this.Activated += Window_Activated;
                ((FrameworkElement)this.Content).ActualThemeChanged += Window_ThemeChanged;

                _configurationSource.IsInputActive = true;
                SetConfigurationSourceTheme();
            }

            // clean up any existing active controllers before applying a new one
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

        private void Window_ThemeChanged(FrameworkElement sender, object args)
        {
            SetConfigurationSourceTheme();
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
        // fix: after window recreation, briefly kick the backdrop pipeline (None -> Mica/Acrylic) to force a DWM
        // compositor refresh
        private void KickBackdropRefresh()
        {
            if (_isClosed) return;

            string currentBackdrop = SettingsService.Instance.BackdropType;
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
