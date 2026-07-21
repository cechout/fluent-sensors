using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using WinRT;
using System.Runtime.InteropServices;
using System.Linq;

using FluentSensors.Persistence.Services;
using FluentSensors.Persistence.Models;
using FluentSensors.Controls.SensorRow;


namespace FluentSensors.Features.Widget
{
    public sealed partial class WidgetWindow : Window
    {
        // === win32 api imports ===

        // import the Windows-API to calculate the screen scaling (100%, 125%, 150% etc.)
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);


        // === fields ===

        private AppWindow _appWindow;
        private const string WindowKey = "Widget"; 
        public WidgetViewModel ViewModel { get; }
        public static WidgetWindow CurrentInstance { get; private set; }
        public static event Action WidgetStateChanged;
        private static WidgetWindow _retainedInstance;

        // system backdrop controllers and configuration
        private DesktopAcrylicController _acrylicController;
        private MicaController _micaController;
        private SystemBackdropConfiguration _configurationSource;


        // === constructor ===

        // accepts the list of selected sensors from SensorsPage.xaml.cs
        public WidgetWindow(List<SensorRowViewModel> selectedSensors)
        {
            // initialization
            ViewModel = new WidgetViewModel(selectedSensors); // pass the selected sensors down to the ViewModel layer
            this.InitializeComponent();
            this.AppWindow.SetIcon("Assets\\Icon\\Icon.ico");
            CurrentInstance = this;
            WidgetStateChanged?.Invoke();

            // window configuration
            _appWindow = this.AppWindow;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomTitleBar);
            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = true; // replaces the CompactOverlay behavior
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
            presenter.IsResizable = true;
            _appWindow.SetPresenter(presenter);

            // window size and position:
            // restore the last saved X/Y/Width if one exists; this covers both the auto-reopen-on-launch case and manually
            // re-pinning sensors while the app is running
            // Height is always recalculated from the current sensor count, since it depends on how many sensors are pinned
            // right now, not on what was pinned when the position was last saved
            // PositionWidgetTopRight is only the fallback for the very first time the widget is ever created, when there is
            // no saved state yet; it will later also be reused as the target for explicit "pin to corner" buttons
            double scaleFactor = GetScaleFactor();
            var savedState = WindowStateService.Instance.GetState(WindowKey);
            if (savedState != null && IsPositionOnScreen(savedState.X, savedState.Y, savedState.Width, savedState.Height))
            {
                int height = CalculateWidgetHeight(selectedSensors.Count, scaleFactor);
                _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                    savedState.X, savedState.Y, savedState.Width, height));
            }
            else
            {
                ResizeWidgetToFitSensors(selectedSensors.Count);
            }

            // remember this window as open and which sensors are pinned, so it can auto-reopen with the same sensors on
            // next launch
            SaveWindowState(selectedSensors);

            // theming
            SetBackdrop(SettingsService.Instance.BackdropType);
            ApplyTheme(SettingsService.Instance.AppTheme);

            // event routing
            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            SettingsService.Instance.BackdropTypeChanged += OnBackdropTypeChanged;
            SettingsService.Instance.OpacityChanged += OnOpacityChanged;
            SettingsService.Instance.TintColorChanged += OnTintColorChanged;

            this.Closed += WidgetWindow_Closed;
            _appWindow.Changed += AppWindow_Changed;
            _appWindow.Closing += AppWindow_Closing;
        }


        // === public methods ===

        // shows the widget with the given sensors, reusing the previously hidden window instance if one exists instead of
        // creating a new one every time (see _retainedInstance)
        public static void ShowWithSensors(List<SensorRowViewModel> selectedSensors)
        {
            // widget is already open and visible: swap its content and resize in place, no need to touch visibility at all
            if (CurrentInstance != null)
            {
                CurrentInstance.ReconfigureFor(selectedSensors);
                CurrentInstance.Activate();
                return;
            }

            // widget was previously hidden (closed via the X button): reuse that native window instead of creating a new one
            if (_retainedInstance != null)
            {
                var window = _retainedInstance;
                _retainedInstance = null;

                window.ReconfigureFor(selectedSensors);
                CurrentInstance = window;
                WidgetStateChanged?.Invoke();

                window._appWindow.Show();
                window.Activate();
                return;
            }

            // no widget has been created this session yet: build a fresh native window
            var newWindow = new WidgetWindow(selectedSensors);
            newWindow.Activate();
        }


        // === lifecycle ===

        private void WidgetWindow_Closed(object sender, WindowEventArgs args)
        {
            // mark the widget as closed so it wont auto-reopen on the next launch; keep the last rect and pinned sensors
            // around in case its simply re-pinned later this session
            SaveWindowState(pinnedSensors: null, wasOpen: false);

            // we detach the event handlers from the settings service
            SettingsService.Instance.BackdropTypeChanged -= OnBackdropTypeChanged;
            SettingsService.Instance.OpacityChanged -= OnOpacityChanged;
            SettingsService.Instance.TintColorChanged -= OnTintColorChanged;
            SettingsService.Instance.ThemeChanged -= OnThemeChanged;

            // detach the event handlers from the static HardwareMonitorService
            ViewModel.Cleanup();

            // dispose system backdrop controllers
            // *also from the official Microsoft documentation
            _acrylicController?.Dispose();
            _acrylicController = null;
            _micaController?.Dispose();
            _micaController = null;

            this.Activated -= Window_Activated;
            _configurationSource = null;
            CurrentInstance = null;
            WidgetStateChanged?.Invoke();

            // if the dashboard was already closed too, there is nothing left to keep the app alive for
            MainWindow.CurrentInstance?.EvaluateFullExit();
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_configurationSource != null)
            {
                // usually, you would set IsInputActive based on whether the window is currently active or not, like this:
                // _configurationSource.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;

                // but that has a big flaw: as soon as the user clicks outside of the widget, it becomes deactivated and the blur
                // disappears, so instead:
                // we force the engine to just aleays render the active blur
                _configurationSource.IsInputActive = true;
            }
        }

        // WinUI 3 never releases secondary Window objects after a real close - confirmed platform bug (see
        // _retainedInstance)
        // Workaround: hide instead of actually closing, and keep this instance around for reuse the next time a sensor set
        // gets pinned
        // Deliberately does NOT dispose backdrop controllers, unsubscribe SettingsService events, or call ViewModel.Cleanup()
        // here; the window stays alive, just hidden, so those stay valid for reuse
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            args.Cancel = true;

            SaveWindowState(pinnedSensors: null, wasOpen: false);
            CurrentInstance = null;
            _retainedInstance = this;
            WidgetStateChanged?.Invoke();

            _appWindow.Hide();

            // if the dashboard was already closed too, there is nothing left to keep the app alive for
            MainWindow.CurrentInstance?.EvaluateFullExit();
        }


        // === user interaction ===

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
                // the app process is safely kept alive by the open widget window
                var newMainWindow = new MainWindow();
                newMainWindow.Activate();
            }

            // this.Close(); // optional: close the widget when returning to dashboard
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

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            // triggers when the widget window minimizes or restores
            if (args.DidPresenterChange && MainWindow.CurrentInstance != null)
            {
                // notify the main window to re-evaluate the system tray state
                MainWindow.CurrentInstance.CheckAndHideToTray();
            }

            // capture position/size for persistence whenever the window moves or resizes
            if ((args.DidPositionChange || args.DidSizeChange) && this.AppWindow.IsVisible)
            {
                SaveWindowState();
            }
        }


        // === window sizing and positioning ===

        // converts the screen DPI to a scale factor
        // (100% = 1.0, 125% = 1.25, etc.)
        private double GetScaleFactor()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            return dpi / 96.0; // 96 is the Windows standard for 100% I guess
        }

        // calculates the widgets physical pixel height based on how many sensors are pinned
        private int CalculateWidgetHeight(int sensorCount, double scaleFactor)
        {
            double desiredXamlHeight = 31 + (sensorCount * (104 + 8)); // titleBar-height + x*(sensor-height + spacing)
            int physicalHeight = (int)(desiredXamlHeight * scaleFactor);

            int screenHeight = DisplayArea.Primary.WorkArea.Height;
            return Math.Min(physicalHeight, screenHeight - 40); // height should not be taller than the screen
        }

        // checks whether the given rect would actually be visible on any currently connected monitor; a saved position can
        // become stale if the monitor it was on gets disconnected, or the display arrangement changes
        private bool IsPositionOnScreen(int x, int y, int width, int height)
        {
            var rect = new Windows.Graphics.RectInt32(x, y, width, height);

            // indexed loop instead of foreach: iterating DisplayArea.FindAll() with foreach throws an InvalidCastException
            // due to a WinRT interop bug in its enumerator; indexer access avoids it
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

        // resizes the window to fit the pinned sensor count, without forcing a specific screen position;
        // used as the fallback when there is no valid saved position (first launch, or the saved monitor is gone)
        private void ResizeWidgetToFitSensors(int sensorCount)
        {
            double scaleFactor = GetScaleFactor();
            int physicalHeight = CalculateWidgetHeight(sensorCount, scaleFactor);

            double desiredXamlWidth = 310;
            int physicalWidth = (int)(desiredXamlWidth * scaleFactor);

            _appWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));
        }

        private void PositionWidgetTopRight(int sensorCount)
        {
            double scaleFactor = GetScaleFactor();

            // get display size (already in physical pixels)
            var displayArea = DisplayArea.Primary;
            int screenWidth = displayArea.WorkArea.Width;

            // our XAML desired width (DIPs), converted to physical pixels for the GPU
            double desiredXamlWidth = 310;
            int physicalWidth = (int)(desiredXamlWidth * scaleFactor);
            int physicalHeight = CalculateWidgetHeight(sensorCount, scaleFactor);

            // move and resize the window
            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                screenWidth - physicalWidth - 10, // 10px margin from the right edge
                10, // 10px margin from the top edge
                physicalWidth,
                physicalHeight));
        }

        // rebuilds the widgets content and resizes the window for a newly selected sensor set, reusing the existing native
        // window instead of tearing it down and creating a new one
        private void ReconfigureFor(List<SensorRowViewModel> selectedSensors)
        {
            ViewModel.Reconfigure(selectedSensors);

            double scaleFactor = GetScaleFactor();
            var savedState = WindowStateService.Instance.GetState(WindowKey);
            if (savedState != null && IsPositionOnScreen(savedState.X, savedState.Y, savedState.Width, savedState.Height))
            {
                int height = CalculateWidgetHeight(selectedSensors.Count, scaleFactor);
                _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                    savedState.X, savedState.Y, savedState.Width, height));
            }
            else
            {
                ResizeWidgetToFitSensors(selectedSensors.Count);
            }

            SaveWindowState(selectedSensors);
        }

        // writes the current rect (debounced) to the window state store
        // pinnedSensors is only passed when the pin selection actually changed (construction); on plain move/resize or close,
        // passing null keeps whatever IDs were already saved
        private void SaveWindowState(List<SensorRowViewModel> pinnedSensors = null, bool wasOpen = true)
        {
            var state = WindowStateService.Instance.GetState(WindowKey) ?? new WindowState();

            // while minimized, Windows reports the windows position as the (-32000, -32000) sentinel value; keep the last
            // known real rect instead of overwriting it with that garbage
            bool isMinimized = this.AppWindow.Presenter is OverlappedPresenter presenter &&
                                presenter.State == OverlappedPresenterState.Minimized;
            if (!isMinimized)
            {
                state.X = _appWindow.Position.X;
                state.Y = _appWindow.Position.Y;
                state.Width = _appWindow.Size.Width;
                state.Height = _appWindow.Size.Height;
            }
            state.WasOpen = wasOpen;

            if (pinnedSensors != null)
            {
                state.PinnedSensorIds = pinnedSensors.Select(s => s.Id).ToList();
            }

            WindowStateService.Instance.SetState(WindowKey, state);
        }


        // === theme and backdrop application ===

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
                // determine the correct color
                Windows.UI.Color targetColor;
                if (SettingsService.Instance.UseAccentColor)
                {
                    // extract the live Windows 11 Accent color from the application resources
                    targetColor = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
                }
                else
                {
                    targetColor = SettingsService.Instance.CustomTintColor;
                }

                // apply all properties in one batch
                _acrylicController.TintColor = targetColor;
                _acrylicController.TintOpacity = SettingsService.Instance.TintOpacity;
                _acrylicController.LuminosityOpacity = SettingsService.Instance.LuminosityOpacity;
            }
        }

        private void UpdateSolidBackground()
        {
            // we intervene only, if "solid" is selected
            if (SettingsService.Instance.BackdropType == "None")
            {
                // mirrors UpdateAcrylicProperties' color resolution; without this, the solid background always used
                // CustomTintColor regardless of the Accent/Custom source setting
                Windows.UI.Color targetColor = SettingsService.Instance.UseAccentColor
                    ? (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]
                    : SettingsService.Instance.CustomTintColor;

                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(targetColor);
            }
        }

        // dynamically applies the chosen backdrop material to the WidgetWindow based on the users selection in the settings page
        // *this code is mainly based on the official Microsoft documentation
        public void SetBackdrop(string backdropType)
        {
            // ensure the system dispatcher queue is ready
            DispatcherQueue.EnsureSystemDispatcherQueue();

            // initialize configuration if it doesnt exist yet
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

            // apply the requested backdrop
            if (backdropType == "Acrylic" && DesktopAcrylicController.IsSupported())
            {
                _acrylicController = new DesktopAcrylicController();
                _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
                _acrylicController.SetSystemBackdropConfiguration(_configurationSource);

                UpdateAcrylicProperties();

                // make the grid transparent, when "acrylic" is selected
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else if (backdropType == "Mica" && MicaController.IsSupported())
            {
                _micaController = new MicaController();
                _micaController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
                _micaController.SetSystemBackdropConfiguration(_configurationSource);

                // make the grid transparent, when "acrylic" is selected
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else
            {
                // color the grid with the solid color, when "none" is selected
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
    }
}