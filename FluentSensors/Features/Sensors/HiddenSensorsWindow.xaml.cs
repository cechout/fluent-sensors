using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using WinRT;
using CommunityToolkit.WinUI.Controls;
using System.Linq;
using FluentSensors.Persistence.Models;
using FluentSensors.Persistence.Services;
using FluentSensors.Common;


namespace FluentSensors.Features.Sensors
{
    public sealed partial class HiddenSensorsWindow : Window
    {
        // === fields ===

        // import the Windows-API to calculate the screen scaling 
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(nint hwnd);

        // window fields
        private AppWindow _appWindow;
        private const string WindowKey = "HiddenSensors"; 

        // system backdrop controller and configuration (Mica only)
        private MicaController _micaController;
        private SystemBackdropConfiguration _configurationSource;

        // public binding surface
        public static HiddenSensorsWindow CurrentInstance { get; private set; }
        public HardwareGroupViewModel HardwareGroup { get; } 
        public string WindowTitleText { get; }
        public SensorsViewModel ViewModel => SensorsViewModel.Instance;


        // === constructor ===

        // accepts the hardware group whose hidden sensors this window displays
        public HiddenSensorsWindow()
        {
            this.InitializeComponent();
            this.AppWindow.SetIcon("Assets\\Icon\\Icon.ico");
            CurrentInstance = this;

            // window configuration
            _appWindow = this.AppWindow;
            ExtendsContentIntoTitleBar = true; 
            SetTitleBar(CustomTitleBar); 
            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = true;

            double scaleFactor = GetScaleFactor();
            presenter.PreferredMinimumWidth = (int)(280 * scaleFactor);
            presenter.PreferredMinimumHeight = (int)(200 * scaleFactor);

            _appWindow.SetPresenter(presenter);

            // restore the last saved position + size if one exists and is still on screen, otherwise fall back to the
            // fixed default size with Windows own default placement
            var savedState = WindowStateService.Instance.GetState(WindowKey);
            if (savedState != null && IsPositionOnScreen(savedState.X, savedState.Y, savedState.Width, savedState.Height))
            {
                _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                    savedState.X, savedState.Y, savedState.Width, savedState.Height));
            }
            else
            {
                SetWindowSize();
            }

            // theming
            SetBackdrop();
            ApplyTheme(SettingsService.Instance.AppTheme);

            SettingsService.Instance.ThemeChanged += OnThemeChanged;
            this.Closed += HiddenSensorsWindow_Closed;
            _appWindow.Closing += AppWindow_Closing;
            _appWindow.Changed += AppWindow_Changed;
            RootGrid.Loaded += RootGrid_Loaded;
        }


        // re-shows a previously hidden instance instead of creating a new one
        public void ShowAndActivate()
        {
            _appWindow.Show();
            this.Activate();
        }


        // === lifecycle event handlers ===

        // --- memory leak: HiddenSensorsWindow never released after close ---
        // problem: WinUI 3 never releases secondary Window objects back to the GC/OS after a real close
        // confirmed, still-open platform bug, reproducible even with empty window content:
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // fix: never actually close this window during normal app runtime; hide it and keep the single instance  alive for
        // the apps lifetime instead, reused on the next open
        // this caps the leak at one instance total instead of it growing unbounded with every open/close cycle
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            args.Cancel = true;
            SaveWindowState();
            _appWindow.Hide();
        }

        private void HiddenSensorsWindow_Closed(object sender, WindowEventArgs args)
        {
            SaveWindowState();

            // explicitly unbind from SensorsViewModel.Instance.HardwareGroups
            HardwareGroupsItemsControl.ItemsSource = null; 

            SettingsService.Instance.ThemeChanged -= OnThemeChanged;
            _appWindow.Changed -= AppWindow_Changed;
            ((FrameworkElement)this.Content).ActualThemeChanged -= Window_ThemeChanged; // self-cycle within the window itself

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
                // force the engine to always render the active blur, same reasoning as in WidgetWindow
                _configurationSource.IsInputActive = true;
            }
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            // capture position/size for persistence whenever the window moves or resizes
            if ((args.DidPositionChange || args.DidSizeChange) && this.AppWindow.IsVisible)
            {
                SaveWindowState();
            }
        }

        // expands the first group that actually has hidden sensors 
        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            RootGrid.Loaded -= RootGrid_Loaded; // only ever needed once per window instance

            // safety net, not a confirmed active bug:
            // setting IsExpanded synchronously here, while the ItemsControl above is still building the SettingsExpander/
            // ItemsRepeater tree for the very first time, can re-enter XAMLs layout pass while its already running; XAML
            // sometimes treats that as fatal (reentrancy fail-fast) instead of a normal exception
            // deferring to the next dispatcher cycle lets the current layout pass finish first; unclear whether this is still
            // reachable now that the ItemsSource crash above is worked around separately; kept anyway, costs nothing and
            // the crash mode was severe when it happened
            this.DispatcherQueue.TryEnqueue(() =>
            {
                var firstGroupWithHidden = ViewModel.HardwareGroups.FirstOrDefault(g => g.HasHiddenSensors);
                if (firstGroupWithHidden != null)
                {
                    firstGroupWithHidden.IsExpanded = true;
                }
            });
        }


        // === user interaction ===

        private void RestoreSelected_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RestoreSelectedHiddenSensors();
            this.Close();
        }


        // === settings event listeners and handlers ===

        private void OnThemeChanged(string newTheme)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                ApplyTheme(newTheme);
            });
        }


        // === core logic for theme and material application ===

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
            // if Mica isnt supported, RootGrid keeps its themed fallback background set in XAML
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


        // === private helper methods ===

        // sets a fixed default size for the window; position is left to Windows own default placement
        private void SetWindowSize()
        {
            double scaleFactor = GetScaleFactor();

            double desiredXamlWidth = 340;
            double desiredXamlHeight = 500;

            int physicalWidth = (int)(desiredXamlWidth * scaleFactor);
            int physicalHeight = (int)(desiredXamlHeight * scaleFactor);

            _appWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));
        }

        // converts the screen DPI to a scale factor (100% = 1.0, 125% = 1.25, etc.), same helper as in WidgetWindow
        private double GetScaleFactor()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            return dpi / 96.0;
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

        // writes the current rect to the window state store
        private void SaveWindowState()
        {
            var state = WindowStateService.Instance.GetState(WindowKey) ?? new WindowState();

            state.X = _appWindow.Position.X;
            state.Y = _appWindow.Position.Y;
            state.Width = _appWindow.Size.Width;
            state.Height = _appWindow.Size.Height;

            WindowStateService.Instance.SetState(WindowKey, state);
        }

        // helper method to fix rendering of items
        private void SettingsExpander_Loaded(object sender, RoutedEventArgs e)
        {
            SettingsExpanderRepaintFix.Attach((SettingsExpander)sender);
        }
    }
}