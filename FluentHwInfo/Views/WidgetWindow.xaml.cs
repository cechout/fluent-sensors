using FluentHwInfo.Services;
using FluentHwInfo.ViewModels;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WinRT;
using System.Runtime.InteropServices;
using WinUIEx;
using WindowState = FluentHwInfo.Models.WindowState;
using System.Linq;

namespace FluentHwInfo.Views
{
    public sealed partial class WidgetWindow : Window
    {
        private AppWindow _appWindow;
        private const string WindowKey = "Widget"; // key under which this windows state is saved
        public WidgetViewModel ViewModel { get; } // expose the ViewModel so {x:Bind} in XAML can access it
        public static WidgetWindow CurrentInstance { get; private set; }

        // system backdrop controllers and configuration
        private DesktopAcrylicController _acrylicController;
        private MicaController _micaController;
        private SystemBackdropConfiguration _configurationSource;

        // import the Windows-API to calculate the screen scaling (100%, 125%, 150% etc.)
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);


        // constructor accepts the list of selected sensors from SensorsPage.xaml.cs
        public WidgetWindow(List<SensorRowViewModel> selectedSensors)
        {
            // initialization
            ViewModel = new WidgetViewModel(selectedSensors); // pass the selected sensors down to the ViewModel layer
            this.InitializeComponent();
            this.AppWindow.SetIcon("Assets\\Icon\\Icon.ico");
            CurrentInstance = this;

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

            // window size and position: restore the last saved rect if the widget was open before,
            // otherwise fall back to the original top-right auto-positioning
            var savedState = WindowStateService.Instance.GetState(WindowKey);
            if (savedState != null && savedState.WasOpen)
            {
                _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                savedState.X, savedState.Y, savedState.Width, savedState.Height));
            }
            else
            {
                PositionWidgetTopRight(selectedSensors.Count); // we pass the number of sensors to the method for auto-sizing
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
        }


        // general window settings
        private void PositionWidgetTopRight(int sensorCount)
        {
            // get window-handle and scale factor (DPI) 
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            double scaleFactor = dpi / 96.0; // 96 is the Windows standard for 100% I guess

            // get display size (already in physical pixels)
            var displayArea = DisplayArea.Primary;
            int screenWidth = displayArea.WorkArea.Width;
            int screenHeight = displayArea.WorkArea.Height;

            // our XAML desired sizes (DIPs)
            double desiredXamlWidth = 310; // width
            double desiredXamlHeight = 31 + (sensorCount * (104 + 8)); // height: titleBar-height + x*(sensor-height + spacing)

            // convert to physical pixels for the GPU based on the dpi scale factor
            int physicalWidth = (int)(desiredXamlWidth * scaleFactor);
            int physicalHeight = (int)(desiredXamlHeight * scaleFactor);
            physicalHeight = Math.Min(physicalHeight, screenHeight - 40); // height should not be taller than the screen

            // move and resize the window
            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                screenWidth - physicalWidth - 10, // 10px margin from the right edge
                10,                               // 10px margin from the top edge
                physicalWidth,
                physicalHeight));
        }
        // writes the current rect (debounced) to the window state store
        // pinnedSensors is only passed when the pin selection actually changed (construction); on plain move/resize or close,
        // passing null keeps whatever IDs were already saved
        private void SaveWindowState(List<SensorRowViewModel> pinnedSensors = null, bool wasOpen = true)
        {
            var state = WindowStateService.Instance.GetState(WindowKey) ?? new WindowState();

            state.X = _appWindow.Position.X;
            state.Y = _appWindow.Position.Y;
            state.Width = _appWindow.Size.Width;
            state.Height = _appWindow.Size.Height;
            state.WasOpen = wasOpen;

            if (pinnedSensors != null)
            {
                state.PinnedSensorIds = pinnedSensors.Select(s => s.Id).ToList();
            }

            WindowStateService.Instance.SetState(WindowKey, state);
        }
        private void WidgetWindow_Closed(object sender, WindowEventArgs args)
        {
            // mark the widget as closed so it wont auto-reopen on the next launch; keep the last rect and pinned sensors
            // around in case it's simply re-pinned later this session
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


        // user iteraction
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


        // settings event listeners and handlers
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


        // core logic for theme and material application
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
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(SettingsService.Instance.CustomTintColor);
            }
        }


        // system backdrop logic functions
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