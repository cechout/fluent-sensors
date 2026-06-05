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

        // Expose the ViewModel so {x:Bind} in XAML can access it.
        public WidgetViewModel ViewModel { get; }

        // system backdrop controllers and configuration
        private DesktopAcrylicController _acrylicController;
        private MicaController _micaController;
        private SystemBackdropConfiguration _configurationSource;

        // import the Windows-API to calculate the screen scaling (100%, 125%, 150% etc.)
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);


        public WidgetWindow(List<SensorRowViewModel> selectedSensors)
        {
            // pass the selected sensors down to the ViewModel layer
            ViewModel = new WidgetViewModel(selectedSensors);

            this.InitializeComponent();

            // Start the backdrop engine with the user's saved preference
            SetBackdrop(SettingsService.Instance.BackdropType);

            // listen to the global settings
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


        // backdrop-related event handlers: whenever the user changes a setting in the settings page, the WidgetWindow receives
        // an event and applies the new backdrop settings immediately
        private void OnBackdropTypeChanged(string newType)
        {
            // since the event can come from another window, we make sure to run on the UI thread
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

        // This method fixes the WinUI rendering bug by forcefully applying all values simultaneously.
        private void UpdateAcrylicProperties()
        {
            if (_acrylicController != null)
            {
                // 1. Determine the correct color
                Windows.UI.Color targetColor;
                if (SettingsService.Instance.UseAccentColor)
                {
                    // Extract the live Windows 11 Accent Color from the application resources
                    targetColor = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
                }
                else
                {
                    targetColor = SettingsService.Instance.CustomTintColor;
                }

                // 2. Apply all properties in one batch
                _acrylicController.TintColor = targetColor;
                _acrylicController.TintOpacity = SettingsService.Instance.TintOpacity;
                _acrylicController.LuminosityOpacity = SettingsService.Instance.LuminosityOpacity;

                // 3. Optional fallback: Force the compositor to re-evaluate the configuration
                if (_configurationSource != null)
                {
                    _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
                }
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


        private void WidgetWindow_Closed(object sender, WindowEventArgs args)
        {
            // detach the event handlers from the static HardwareMonitorService
            ViewModel.Cleanup();

            // dispose system backdrop controllers
            // also from the official Microsoft documentation
            _acrylicController?.Dispose();
            _acrylicController = null;
            _micaController?.Dispose();
            _micaController = null;

            this.Activated -= Window_Activated;
            _configurationSource = null;
        }

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
            double desiredXamlHeight = 31 + (sensorCount * (106 + 8)); // height: titleBar (48?) + Padding(?) + (Sensors * 130) + Buffer(?)

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

            // close widget window
            this.Close();
        }


        // dynamically applies the chosen backdrop material to the WidgetWindow based on the user's selection in the settings page
        // this code is mainly based on the official Microsoft documentation
        public void SetBackdrop(string backdropType)
        {
            // Ensure the system dispatcher queue is ready
            DispatcherQueue.EnsureSystemDispatcherQueue();

            // 1. Initialize configuration if it doesn't exist yet
            if (_configurationSource == null)
            {
                _configurationSource = new SystemBackdropConfiguration();
                this.Activated += Window_Activated;
                ((FrameworkElement)this.Content).ActualThemeChanged += Window_ThemeChanged;

                _configurationSource.IsInputActive = true;
                SetConfigurationSourceTheme();
            }

            // 2. Clean up any existing active controllers before applying a new one
            _acrylicController?.Dispose();
            _acrylicController = null;
            _micaController?.Dispose();
            _micaController = null;

            // 3. Apply the requested backdrop
            if (backdropType == "Acrylic" && DesktopAcrylicController.IsSupported())
            {
                _acrylicController = new DesktopAcrylicController();
                _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
                _acrylicController.SetSystemBackdropConfiguration(_configurationSource);

                UpdateAcrylicProperties();

                // we make the grid transparent, when "acrylic" is selected
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else if (backdropType == "Mica" && MicaController.IsSupported())
            {
                _micaController = new MicaController();
                _micaController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
                _micaController.SetSystemBackdropConfiguration(_configurationSource);

                // we make the grid transparent, when "acrylic" is selected
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else 
            {
                // we color the grid with the solid color, when "none" is selected
                UpdateSolidBackground();
            }
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_configurationSource != null)
            {
                // usually, you would set IsInputActive based on whether the window is currently active or not, like this:
                // _configurationSource.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;

                // but that has a big flaw: as soon as the user clicks outside of the widget, it becomes deactivated and the blur
                // disappears, so instead:
                // we force the engine to ALWAYS render the active blur
                // no matter where the user clicks, the widget tells the graphics card: "I am active!"
                _configurationSource.IsInputActive = true;
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