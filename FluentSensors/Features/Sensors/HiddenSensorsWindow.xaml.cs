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
using FluentSensors.Common.Sensors;
using FluentSensors.Common.UI;


namespace FluentSensors.Features.Sensors
{
    public sealed partial class HiddenSensorsWindow : Window
    {
        // === fields ===

        // screen scaling
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(nint hwnd);

        private AppWindow _appWindow;
        private const string WindowKey = "HiddenSensors";

        // system backdrop, Mica only
        private MicaController _micaController;
        private SystemBackdropConfiguration _configurationSource;

        // public binding surface
        public static HiddenSensorsWindow CurrentInstance { get; private set; }
        public HardwareGroupViewModel HardwareGroup { get; }
        public string WindowTitleText { get; }
        public SensorsViewModel ViewModel => SensorsViewModel.Instance;


        // === constructor ===

        public HiddenSensorsWindow()
        {
            this.InitializeComponent();
            this.AppWindow.SetIcon("Assets\\Icon\\Icon.ico");
            CurrentInstance = this;

            // a click on empty space hides the keyboard focus rectangle again
            PointerFocusReset.Attach(Content);

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

            // the saved rect when it is on a connected monitor, otherwise the default size and Windows placement
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


        // shows the hidden instance again
        public void ShowAndActivate()
        {
            _appWindow.Show();
            this.Activate();
        }


        // === lifecycle event handlers ===

        // --- memory leak: HiddenSensorsWindow never released after close ---
        // problem: WinUI 3 never releases a closed secondary Window (confirmed, still open, even with empty content):
        // https://github.com/microsoft/microsoft-ui-xaml/issues/9063
        // fix: hide instead of closing and reuse the one instance for the session, so the leak stays at one
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            args.Cancel = true;
            SaveWindowState();
            _appWindow.Hide();
        }

        private void HiddenSensorsWindow_Closed(object sender, WindowEventArgs args)
        {
            SaveWindowState();

            // unbinds from SensorsViewModel.Instance.HardwareGroups
            HardwareGroupsItemsControl.ItemsSource = null;

            SettingsService.Instance.ThemeChanged -= OnThemeChanged;
            _appWindow.Changed -= AppWindow_Changed;
            ((FrameworkElement)this.Content).ActualThemeChanged -= Window_ThemeChanged;

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
                // always active, like WidgetWindow
                _configurationSource.IsInputActive = true;
            }
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if ((args.DidPositionChange || args.DidSizeChange) && this.AppWindow.IsVisible)
            {
                SaveWindowState();
            }
        }

        // expands the first group with hidden sensors
        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            RootGrid.Loaded -= RootGrid_Loaded; // once per instance

            // deferred one dispatcher cycle: IsExpanded while the ItemsControl builds its first tree can re-enter the
            // running layout pass, which XAML may treat as a fail-fast (a safety net, not a confirmed active bug)
            this.DispatcherQueue.TryEnqueue(() =>
            {
                var firstGroupWithHidden = ViewModel.HardwareGroups.FirstOrDefault(g => g.HasHiddenSensors);
                if (firstGroupWithHidden != null)
                {
                    firstGroupWithHidden.IsExpandedInHiddenWindow = true;
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


        // === theme and material ===

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

        // Mica where supported; (Windows turns it off with transparency effects itself)
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

                // transparent, so the material shows
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            // without Mica the XAML fallback background stays
        }

        private void Window_ThemeChanged(FrameworkElement sender, object args)
        {
            SetConfigurationSourceTheme();

            // the group icons are plain brushes; the groups are shared with the sensors page, so
            // this covers it while unloaded
            HardwareColorMode.IsDarkTheme = sender.ActualTheme == ElementTheme.Dark;
            SensorsViewModel.Instance.RefreshGroupIconBrushes();
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


        // === private helpers ===

        // the default size; Windows places the window
        private void SetWindowSize()
        {
            double scaleFactor = GetScaleFactor();

            double desiredXamlWidth = AppSettingsData.HiddenSensorsWindowDefaultWidthDip;
            double desiredXamlHeight = AppSettingsData.HiddenSensorsWindowDefaultHeightDip;

            int physicalWidth = (int)(desiredXamlWidth * scaleFactor);
            int physicalHeight = (int)(desiredXamlHeight * scaleFactor);

            _appWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));
        }

        private double GetScaleFactor()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            return dpi / 96.0;
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

        private void SaveWindowState()
        {
            var state = WindowStateService.Instance.GetState(WindowKey) ?? new WindowState();

            state.X = _appWindow.Position.X;
            state.Y = _appWindow.Position.Y;
            state.Width = _appWindow.Size.Width;
            state.Height = _appWindow.Size.Height;

            WindowStateService.Instance.SetState(WindowKey, state);
        }

        // see SettingsExpanderRepaintFix
        private void SettingsExpander_Loaded(object sender, RoutedEventArgs e)
        {
            SettingsExpanderRepaintFix.Attach((SettingsExpander)sender);
        }
    }
}
