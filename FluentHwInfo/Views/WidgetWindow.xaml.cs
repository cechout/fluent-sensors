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

namespace FluentHwInfo.Views
{
    public sealed partial class WidgetWindow : Window
    {
        private AppWindow _appWindow;
        public WidgetViewModel ViewModel { get; } // expose the ViewModel so {x:Bind} in XAML can access it

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
            // pass the selected sensors down to the ViewModel layer
            ViewModel = new WidgetViewModel(selectedSensors);

            this.InitializeComponent();

            SetBackdrop(SettingsService.Instance.BackdropType);
            ApplyTheme(SettingsService.Instance.AppTheme);

            // listen to the global settings
            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            SettingsService.Instance.BackdropTypeChanged += OnBackdropTypeChanged;
            SettingsService.Instance.OpacityChanged += OnOpacityChanged;
            SettingsService.Instance.TintColorChanged += OnTintColorChanged;

            // custom window settings
            _appWindow = this.AppWindow;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomTitleBar);

            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = true; // replaces the CompactOverlay behavior
            presenter.IsMaximizable = false; // no fullscreen button
            presenter.IsMinimizable = false;  // no minimized button
            presenter.IsResizable = true; // but our boy is now resizable
            _appWindow.SetPresenter(presenter);

            // we pass the number of sensors to the method for auto-sizing
            PositionWidgetTopRight(selectedSensors.Count);

            // register the closed event to prevent memory leaks in the background service
            this.Closed += WidgetWindow_Closed;
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
            double desiredXamlHeight = 30 + (sensorCount * (104 + 8)); // height: titleBar-height + x*(sensor-height + spacing)

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
        private void WidgetWindow_Closed(object sender, WindowEventArgs args)
        {
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
            // check if the main window is still open; 
            if (MainWindow.CurrentInstance != null)
            {
                // if yes, just bring it to the front
                MainWindow.CurrentInstance.Activate();
            }
            else
            {
                // if not, we just create a new one (the app is still running because the WidgetWindow is open, so
                // we dont need to worry about the shutdown mode)
                var newMainWindow = new MainWindow();
                newMainWindow.Activate();
            }

            // close widget window (do we want this?)
            // this.Close();
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