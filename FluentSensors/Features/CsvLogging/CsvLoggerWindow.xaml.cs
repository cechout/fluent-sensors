using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using WinRT;

using FluentSensors.Common.UI;
using FluentSensors.Controls.SensorRow;
using FluentSensors.Features.TaskbarWidget;
using FluentSensors.Persistence.Models;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.CsvLogging
{
    // the csv logger:
    // a small always-on-top readout for a recording; start, stop, running time, sensor count and rows written
    // chrome, backdrop and retained-instance lifecycle follow WidgetWindow (there is no shared window base, every
    // window carries its own copy)
    public sealed partial class CsvLoggerWindow : Window
    {
        // === win32 api imports ===

        // screen scaling
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);


        // === fields ===

        private AppWindow _appWindow;
        private const string WindowKey = "CsvLogger";

        // the drag floor, also the width the height is estimated at before the first layout pass; the width
        // belongs to the user, the height is read off the arranged rows (the fallback covers an empty
        // measure before the content exists)
        private const double MinWindowWidthDip = 225;
        private const double FallbackWindowHeightDip = 232;

        // status line under main bar
        private const bool ShowStatusLine = false;

        // the lower region (readout and save location); view state only
        private bool _isExpanded = true;

        // ApplyWindowSize re-entry guard; (its resize raises the SizeChanged that calls it)
        private bool _isApplyingSize;

        public CsvLoggerViewModel ViewModel { get; }
        public static CsvLoggerWindow CurrentInstance { get; private set; }
        private static CsvLoggerWindow _retainedInstance;

        private bool _isClosed = false;
        private static bool _isRecreating = false;

        // system backdrop
        private DesktopAcrylicController _acrylicController;
        private MicaController _micaController;
        private SystemBackdropConfiguration _configurationSource;
        private Windows.UI.ViewManagement.UISettings? _uiSettings;


        // === constructor ===

        public CsvLoggerWindow()
        {
            ViewModel = new CsvLoggerViewModel();
            this.InitializeComponent();
            this.AppWindow.SetIcon("Assets\\Icon\\Icon.ico");
            CurrentInstance = this;

            // a click on empty space hides the keyboard focus rectangle again
            PointerFocusReset.Attach(Content);

            // window configuration
            _appWindow = this.AppWindow;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomTitleBar);

            // the back button sits in the drag region and needs its own passthrough rect
            CustomTitleBar.Loaded += (s, e) => UpdateTitleBarPassthroughRegions();
            CustomTitleBar.SizeChanged += (s, e) => UpdateTitleBarPassthroughRegions();

            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = true; // like the widget, readable over other apps
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
            presenter.IsResizable = true; // width only, see ApplyWindowSize
            _appWindow.SetPresenter(presenter);

            // content state before the first measure; (status line and expand state change the height)
            StatusTextBlock.Visibility = ShowStatusLine ? Visibility.Visible : Visibility.Collapsed;

            // a StackPanel keeps the Spacing for a collapsed child too, so the gap goes with the status line
            UpperRegion.Spacing = ShowStatusLine ? UpperRegion.Spacing : 0;

            ApplyExpandState();
            ApplyInitialWidth();

            // the height follows the content (the width was restored above); the position only when it lands on a
            // connected monitor, otherwise Windows places the window
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

            // both regions sit in Auto rows, so re-applying the size on their change keeps the
            // window as tall as its content
            UpperRegion.SizeChanged += OnRegionSizeChanged;
            LowerRegion.SizeChanged += OnRegionSizeChanged;

            this.Closed += CsvLoggerWindow_Closed;
            _appWindow.Changed += AppWindow_Changed;
            _appWindow.Closing += AppWindow_Closing;

            KickBackdropRefresh();
        }


        // === public methods ===

        // opens the logger with the given sensors, reusing the hidden _retainedInstance if there is one
        // (same pattern as WidgetWindow)
        // a running recording keeps its sensor set, its columns are already in the file header; the window says why
        public static void ShowWithSensors(List<SensorRowViewModel> selectedSensors)
        {
            bool isSelectionLocked = CsvLoggingService.Instance.IsRunning;
            if (!isSelectionLocked)
            {
                CsvLoggingService.Instance.SetSensors(selectedSensors);
            }

            // already open
            if (CurrentInstance != null)
            {
                if (isSelectionLocked)
                {
                    CurrentInstance.ViewModel.ShowSelectionLockedHint();
                }

                CurrentInstance.RestoreAndActivate();
                return;
            }

            // hidden by its X: reuse that window
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

            // none yet this session
            var newWindow = new CsvLoggerWindow();
            if (isSelectionLocked)
            {
                newWindow.ViewModel.ShowSelectionLockedHint();
            }
            newWindow.Activate();
        }

        // brings the logger back without touching the recording or its sensors, for the tray single click and menu
        // entry; a logger closed with X has no CurrentInstance and stays closed
        public static void RestoreIfOpen()
        {
            CurrentInstance?.RestoreAndActivate();
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

            // detached first, or AppWindow_Closing cancels the Close below and brings this window back as
            // _retainedInstance; CsvLoggerWindow_Closed stays, it does the real teardown
            try
            {
                _appWindow.Closing -= AppWindow_Closing;
                _appWindow.Changed -= AppWindow_Changed;
                UpperRegion.SizeChanged -= OnRegionSizeChanged;
                LowerRegion.SizeChanged -= OnRegionSizeChanged;
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

        // destroys and rebuilds the logger on an OS theme or transparency change; safe mid-recording, CsvLoggingService
        // owns the file and the counters
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
                // a fallback queue for a null MainWindow.CurrentInstance; (skipping the finally would latch
                // _isRecreating and the logger would never rebuild again)
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

            // settings events
            SettingsService.Instance.BackdropTypeChanged -= OnBackdropTypeChanged;
            SettingsService.Instance.OpacityChanged -= OnOpacityChanged;
            SettingsService.Instance.TintColorChanged -= OnTintColorChanged;
            SettingsService.Instance.ThemeChanged -= OnThemeChanged;

            UpperRegion.SizeChanged -= OnRegionSizeChanged;
            LowerRegion.SizeChanged -= OnRegionSizeChanged;
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
                // always active, or a click outside drops the blur to the inactive material
                // while the logger stays on top
                _configurationSource.IsInputActive = true;
            }
        }

        // --- memory leak: CsvLoggerWindow never released after close ---
        // problem: WinUI 3 never releases a closed secondary Window (confirmed, still open, even with empty content):
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // fix: hide instead of closing and keep the instance as _retainedInstance (same as
        // WidgetWindow and HiddenSensorsWindow)
        // a running recording lives in CsvLoggingService, so closing only stops the readout
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // SafeDestroy is closing for real; a closed window in _retainedInstance would ignore every
            // later SetBackdrop and ApplyTheme
            if (_isClosed) return;

            args.Cancel = true;

            SaveWindowState();
            CurrentInstance = null;
            _retainedInstance = this;

            _appWindow.Hide();
            ViewModel.SetReadoutActive(false);

            // the window survives its close, so the last recordings counters are cleared here (a
            // no-op while one is running)
            CsvLoggingService.Instance.ResetCompletedRecording();
        }

        private void OnRegionSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isClosed) return;

            ApplyWindowSize();
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            // minimize and restore show up as DidSizeChange, not DidPresenterChange
            if (args.DidSizeChange)
            {
                bool isMinimized = sender.Presenter is OverlappedPresenter presenter &&
                                   presenter.State == OverlappedPresenterState.Minimized;
                ViewModel.SetReadoutActive(!isMinimized);

                // a dragged width survives the next launch, like the position
                if (!isMinimized && this.AppWindow.IsVisible)
                {
                    SaveWindowState();
                }
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

        private void TogglePause_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.TogglePause();
        }

        // collapses to the divider, so a recording can sit on screen as a thin bar with the stop button
        private void ToggleDetails_Click(object sender, RoutedEventArgs e)
        {
            _isExpanded = !_isExpanded;

            ApplyExpandState();
            ApplyWindowSize();
            SaveWindowState();
        }

        // opens the folder in the shell; a missing one is not created here, the first start does that
        private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            string folder = ViewModel.LogFolderText;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch { /* no handler registered for the path, nothing to do about that from here */ }
        }

        private void PickLogFolder_Click(object sender, RoutedEventArgs e)
        {
            // owned by this window, so it cannot end up behind the always-on-top logger
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            string picked = Win32FileDialogHelper.PickFolder(hwnd, "CSV Logging Folder", ViewModel.LogFolderText);
            if (picked == null) return; // user cancelled

            ViewModel.SetLogFolder(picked);
        }

        // at Low priority, after the bar is laid out (like MainWindow)
        private void UpdateTitleBarPassthroughRegions()
        {
            this.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,
                () => TitleBarPassthrough.Apply(this, CustomTitleBar, BackToDashboardButton));
        }

        private void BackToDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.CurrentInstance != null)
            {
                MainWindow.CurrentInstance.OpenDashboard();
            }
            else
            {
                // no main window left; the open logger keeps the process alive
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

        // named so SafeDestroy can detach it (every rebuilt window subscribes anew); goes through the router, so a pure
        // accent change refreshes in place
        private void OnSystemVisualSettingsChanged(Windows.UI.ViewManagement.UISettings sender, object args)
        {
            TaskbarFlyoutWindow.RouteSystemVisualsChange(sender);
        }


        // === window sizing and positioning ===

        private double GetScaleFactor()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            return dpi / 96.0;
        }

        // shows or hides the lower region; the chevron points at what the next click does
        private void ApplyExpandState()
        {
            LowerRegion.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;

            // ChevronUp and ChevronDown, escaped to keep the source ascii
            ToggleDetailsIcon.Glyph = _isExpanded ? "\uE70E" : "\uE70D";
            string toggleLabel = _isExpanded ? "Hide details" : "Show details";
            ToolTipService.SetToolTip(ToggleDetailsButton, toggleLabel);
            AutomationProperties.SetName(ToggleDetailsButton, toggleLabel);
        }

        // one row as arranged, margin included (ActualHeight leaves it out, the RootGrid row reserves it)
        private static double RowHeightDip(FrameworkElement row)
        {
            if (row.Visibility != Visibility.Visible) return 0;

            return row.ActualHeight + row.Margin.Top + row.Margin.Bottom;
        }

        // the height the content needs, summed from the four arranged rows; (a standalone Measure reports the
        // unconstrained height, too tall for the wrapping rows)
        private double ContentHeightDip()
        {
            // --- workaround: CommunityToolkit WrapPanel throws when measured at zero width ---
            // problem: WrapPanel.MeasureOverride subtracts its Padding without clamping, so a measure at width 0 builds
            // a negative Size and throws ArgumentOutOfRangeException; 0 is what the window reports before it is first
            // shown, where the constructor calls this
            // fix: force the layout pass only once the content has a width, until then estimate from a
            // measure at the target width
            if (RootGrid.ActualWidth > 0)
            {
                // synchronous, so the rows report the state after a collapse or a status line change
                RootGrid.UpdateLayout();

                double arranged = RowHeightDip(CustomTitleBar) + RowHeightDip(UpperRegion) +
                                  RowHeightDip(Divider) + RowHeightDip(LowerRegion);
                if (arranged > 0) return arranged;
            }

            // pre-layout estimate, a little generous; the first real layout pass corrects it through SizeChanged
            RootGrid.InvalidateMeasure();
            RootGrid.Measure(new Windows.Foundation.Size(MinWindowWidthDip, double.PositiveInfinity));

            double measured = RootGrid.DesiredSize.Height;
            return measured > 0 ? measured : FallbackWindowHeightDip;
        }

        // the saved width or the default; (once, from the constructor, every later width comes from a drag)
        private void ApplyInitialWidth()
        {
            double scaleFactor = GetScaleFactor();
            int frameWidthPx = Math.Max(0, _appWindow.Size.Width - _appWindow.ClientSize.Width);
            int minWidthPx = (int)Math.Round(MinWindowWidthDip * scaleFactor) + frameWidthPx;
            int defaultWidthPx = (int)Math.Round(AppSettingsData.CsvLoggerWindowDefaultWidthDip * scaleFactor)
                + frameWidthPx;

            // below it the readout wraps onto extra rows
            WinUIEx.WindowManager.Get(this).MinWidth = minWidthPx / scaleFactor;

            // a default set below the minimum is lifted to it, the drag floor would refuse it anyway
            var savedState = WindowStateService.Instance.GetState(WindowKey);
            int widthPx = savedState != null && savedState.Width >= minWidthPx
                ? savedState.Width
                : Math.Max(defaultWidthPx, minWidthPx);

            _appWindow.Resize(new Windows.Graphics.SizeInt32(widthPx, _appWindow.Size.Height));
        }

        // pins the window height to the content, leaving the width alone
        private void ApplyWindowSize()
        {
            if (_isApplyingSize) return;
            _isApplyingSize = true;

            try
            {
                double scaleFactor = GetScaleFactor();

                // frame size read off the window; (ExtendsContentIntoTitleBar puts the caption in the client
                // area, so it is not added on top)
                int frameHeightPx = Math.Max(0, _appWindow.Size.Height - _appWindow.ClientSize.Height);
                int heightPx = (int)Math.Round(ContentHeightDip() * scaleFactor) + frameHeightPx;

                // min and max height end up in WM_GETMINMAXINFO, so a drag can only change the width
                var manager = WinUIEx.WindowManager.Get(this);
                manager.MinHeight = heightPx / scaleFactor;
                manager.MaxHeight = heightPx / scaleFactor;

                _appWindow.Resize(new Windows.Graphics.SizeInt32(_appWindow.Size.Width, heightPx));
            }
            finally
            {
                _isApplyingSize = false;
            }
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

        // whether the rect is on a connected monitor; (a saved position goes stale when its monitor is gone)
        private bool IsPositionOnScreen(int x, int y, int width, int height)
        {
            var rect = new Windows.Graphics.RectInt32(x, y, width, height);

            // indexed loop; foreach over DisplayArea.FindAll() throws an InvalidCastException (WinRT enumerator bug)
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

        // writes the rect (debounced); WasOpen stays untouched, the logger never reopens itself on launch
        private void SaveWindowState()
        {
            var state = WindowStateService.Instance.GetState(WindowKey) ?? new WindowState();

            // a minimized window reports (-32000, -32000); the last real rect is kept
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

            // only for the solid material ("None")
            if (SettingsService.Instance.BackdropType == "None")
            {
                Windows.UI.Color targetColor = SettingsService.Instance.UseAccentColor
                    ? (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]
                    : SettingsService.Instance.CustomTintColor;

                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(targetColor);
            }
        }

        // for a pure OS accent change; both calls resolve the accent fresh, no rebuild needed
        public void RefreshAccentSurfaces()
        {
            UpdateAcrylicProperties();
            UpdateSolidBackground();
        }

        // applies the backdrop material from the settings, per the Microsoft guide:
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

            // drop the current controller
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
        // problem: after a Windows transparency or theme change, DesktopAcrylicController needs a rebind to attach its
        // blur to the new DWM swapchain
        // fix: after a rebuild, kick the backdrop once (None, then the current one), with parameters only
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
