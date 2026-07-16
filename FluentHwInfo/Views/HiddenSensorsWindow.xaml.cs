using FluentHwInfo.Services;
using FluentHwInfo.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using WinRT;
using FluentHwInfo.Helpers;
using CommunityToolkit.WinUI.Controls;

namespace FluentHwInfo.Views
{
    public sealed partial class HiddenSensorsWindow : Window
    {
        private AppWindow _appWindow;
        public HardwareGroupViewModel HardwareGroup { get; } // the group this window shows the hidden sensors for
        public string WindowTitleText { get; }
        public SensorsViewModel ViewModel => SensorsViewModel.Instance;

        // system backdrop controller and configuration (Mica only)
        private MicaController _micaController;
        private SystemBackdropConfiguration _configurationSource;

        // import the Windows-API to calculate the screen scaling (100%, 125%, 150% etc.)
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(nint hwnd);


        // constructor accepts the hardware group whose hidden sensors this window displays
        public HiddenSensorsWindow()
        {
            this.InitializeComponent();
            this.AppWindow.SetIcon("Assets\\Icon\\Icon.ico");

            // window configuration
            _appWindow = this.AppWindow;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomTitleBar);
            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = true;
            _appWindow.SetPresenter(presenter);

            SetWindowSize();

            // theming
            SetBackdrop();
            ApplyTheme(SettingsService.Instance.AppTheme);

            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            this.Closed += HiddenSensorsWindow_Closed;
        }


        // general window settings
        // sets a fixed default size for the window; position is left to Windows own default placement
        private void SetWindowSize()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            double scaleFactor = dpi / 96.0; // 96 is the Windows standard for 100% I guess

            double desiredXamlWidth = 340;
            double desiredXamlHeight = 500;

            int physicalWidth = (int)(desiredXamlWidth * scaleFactor);
            int physicalHeight = (int)(desiredXamlHeight * scaleFactor);

            _appWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));
        }
        private void HiddenSensorsWindow_Closed(object sender, WindowEventArgs args)
        {
            SettingsService.Instance.ThemeChanged -= OnThemeChanged;

            _micaController?.Dispose();
            _micaController = null;

            this.Activated -= Window_Activated;
            _configurationSource = null;
        }
        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_configurationSource != null)
            {
                // force the engine to always render the active blur, same reasoning as in WidgetWindow
                _configurationSource.IsInputActive = true;
            }
        }


        // user interaction
        private void RestoreSelected_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RestoreSelectedHiddenSensors();
            this.Close();
        }


        // settings event listeners and handlers
        private void OnThemeChanged(string newTheme)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                ApplyTheme(newTheme);
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

        // applies Mica if the OS supports it; Windows itself disables the blur when the user turns off
        // transparency effects in the system settings, so no extra check for that is needed here
        private void SetBackdrop()
        {
            DispatcherQueue.EnsureSystemDispatcherQueue();

            _configurationSource = new SystemBackdropConfiguration();
            this.Activated += Window_Activated;
            ((FrameworkElement)this.Content).ActualThemeChanged += Window_ThemeChanged;
            _configurationSource.IsInputActive = true;
            SetConfigurationSourceTheme();

            if (MicaController.IsSupported())
            {
                _micaController = new MicaController();
                _micaController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
                _micaController.SetSystemBackdropConfiguration(_configurationSource);

                // make the grid transparent so the Mica material shows through
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            // if Mica isn't supported, RootGrid keeps its themed fallback background set in XAML
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


        // helper method to fix rendering of items
        private void SettingsExpander_Loaded(object sender, RoutedEventArgs e)
        {
            SettingsExpanderRepaintFix.Attach((SettingsExpander)sender);
        }
    }
}